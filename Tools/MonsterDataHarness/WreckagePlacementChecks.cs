using System;
using System.Collections.Generic;
using UnityEngine;

internal static class WreckagePlacementChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static BlockStateStore.InstallationSaveState State(long id, Vector2Int anchor, params Vector2Int[] cells)
        => new() { placementSequence = id, itemId = 11, anchorCoordinate = anchor,
            occupiedCoordinates = new List<Vector2Int>(cells) };
    private static void AddWreck(BlockStateStore store, BlockStateStore.InstallationSaveState state)
        => store.AddWreckage(state, new Vector3(state.anchorCoordinate.x, 0f, state.anchorCoordinate.y), default);

    internal static void Run()
    {
        var store = new BlockStateStore();
        var key = new Vector2Int(4, 8);
        var replacement = State(200L, key, key);
        int revision = store.WreckageRevision;
        Check(store.RemoveWreckageOverlapping(replacement) == 0 && store.WreckageRevision == revision,
            "Ordinary installation without wreckage does no presentation work");
        AddWreck(store, State(100L, key, key));
        AddWreck(store, State(101L, new Vector2Int(5, 8), new Vector2Int(5, 8)));
        revision = store.WreckageRevision;
        Check(store.RemoveWreckageOverlapping(replacement) == 1 && store.Wreckage.Count == 1
            && store.Wreckage[0].placementSequence == 101L && store.WreckageRevision == revision + 1,
            "Replacing a ruined cell clears its wreck but preserves adjacent debris");
        revision = store.WreckageRevision;
        Check(store.RemoveWreckageOverlapping(replacement) == 0 && store.WreckageRevision == revision,
            "Repeated cleanup is idempotent and does not keep rebuilding wreck visuals");
        Check(store.RemoveWreckageOverlapping(null) == 0 && store.WreckageRevision == revision,
            "Invalid placement cannot clear any wreckage");

        var largeWreck = State(102L, new Vector2Int(0, 0), new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0));
        AddWreck(store, largeWreck);
        Check(store.RemoveWreckageOverlapping(State(201L, new Vector2Int(2, 0), new Vector2Int(2, 0))) == 1,
            "Covering any non-anchor footprint cell removes the entire large wreck");
        AddWreck(store, State(103L, new Vector2Int(10, 0), new Vector2Int(10, 0)));
        AddWreck(store, State(104L, new Vector2Int(11, 0), new Vector2Int(11, 0)));
        AddWreck(store, State(105L, new Vector2Int(12, 0), new Vector2Int(12, 0)));
        revision = store.WreckageRevision;
        Check(store.RemoveWreckageOverlapping(State(202L, new Vector2Int(10, 0),
            new Vector2Int(10, 0), new Vector2Int(11, 0), new Vector2Int(12, 0))) == 3
            && store.WreckageRevision == revision + 1 && store.Wreckage.Count == 1,
            "One multi-cell placement clears every overlapped wreck with one visual invalidation");

        AddWreck(store, State(106L, new Vector2Int(20, 0)));
        Check(store.RemoveWreckageOverlapping(State(203L, new Vector2Int(20, 0))) == 1,
            "Legacy empty footprints fall back to the single anchor cell");
        AddWreck(store, State(107L, new Vector2Int(30, 0), new Vector2Int(31, 0)));
        Check(store.RemoveWreckageOverlapping(State(204L, new Vector2Int(30, 0), new Vector2Int(30, 0))) == 0,
            "A declared footprint takes precedence over an anchor outside its actual occupied cells");
        var clearPlacement = State(205L, new Vector2Int(40, 0), new Vector2Int(40, 0));
        store.RemoveWreckageOverlapping(clearPlacement);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) store.RemoveWreckageOverlapping(clearPlacement);
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Wreck overlap checks create no temporary per-placement collections");
        Console.WriteLine($"PASS: {checks} production wreckage replacement checks");
    }
}
