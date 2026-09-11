using System;
using System.Collections.Generic;
using ProjectF.Runtime;

internal static class Program
{
    private struct State
    {
        internal int Count, Gauge, Reserved;
        internal float Growth, Water;
    }

    private static void Main()
    {
        ResourceStateSlots<State> store = new ResourceStateSlots<State>(1);
        var first = store.Allocate(new State { Count = 500, Gauge = 10, Growth = 2, Water = 4 });
        List<ResourceStateSlots<State>.Slot> resources = new List<ResourceStateSlots<State>.Slot>();
        for (int i = 0; i < 10000; i++)
            resources.Add(store.Allocate(new State { Count = i + 1, Gauge = 10, Growth = i % 6 }));
        Require(store.Get(first.Index, first.Generation).Count == 500, "Resizing lost an existing resource.");
        var adjacent = resources[123];
        ref State harvesting = ref store.Get(first.Index, first.Generation);
        harvesting.Count--; harvesting.Gauge = 3; harvesting.Reserved = 2; harvesting.Growth = 3; harvesting.Water = 0;
        Require(store.Get(adjacent.Index, adjacent.Generation).Count == 124, "Harvesting changed a different resource.");
        Require(store.Get(adjacent.Index, adjacent.Generation).Growth == 123 % 6, "Growth changed a different resource.");

        State saved = store.Get(first.Index, first.Generation);
        Require(store.Release(first.Index, first.Generation), "Unload failed.");
        Require(!store.Contains(first.Index, first.Generation), "Unloaded resource remained addressable.");
        Require(!store.Release(first.Index, first.Generation), "A second release changed storage.");
        var planted = store.Allocate(new State { Count = 1, Gauge = 5, Growth = 0 });
        Require(planted.Index == first.Index && planted.Generation != first.Generation, "Slot was not reused with a new generation.");
        Require(store.Get(planted.Index, planted.Generation).Reserved == 0, "A new plant inherited a harvest reservation.");
        Require(!store.Contains(first.Index, first.Generation), "Replanting revived an old handle.");
        try
        {
            store.Get(first.Index, first.Generation).Count = 999;
            throw new Exception("A stale harvest target changed a replanted resource.");
        }
        catch (InvalidOperationException) { }
        Require(store.Get(planted.Index, planted.Generation).Count == 1, "A stale reference modified the new plant.");
        var restored = store.Allocate(saved);
        Require(store.Get(restored.Index, restored.Generation).Gauge == 3 && store.Get(restored.Index, restored.Generation).Growth == 3,
            "Stored state was not retained across unloading/reloading.");
        Require(!store.Contains(0, 0) && !store.Contains(-1, 1) && !store.Contains(int.MaxValue, 1), "An invalid/default handle resolved.");
        foreach (var resource in resources) Require(store.Release(resource.Index, resource.Generation), "Bulk unload failed.");
        store.Release(planted.Index, planted.Generation); store.Release(restored.Index, restored.Generation);
        Require(store.Count == 0, "Bulk unload leaked state.");

        // Fixed seed churn models many overlapping chunk loads, harvest targets and replacements.
        Random random = new Random(73145);
        List<ResourceStateSlots<State>.Slot> live = new List<ResourceStateSlots<State>.Slot>();
        List<ResourceStateSlots<State>.Slot> dead = new List<ResourceStateSlots<State>.Slot>();
        for (int i = 0; i < 50000; i++)
        {
            if (live.Count == 0 || random.Next(2) == 0) live.Add(store.Allocate(new State { Count = i + 1 }));
            else
            {
                int at = random.Next(live.Count); var removed = live[at];
                Require(store.Release(removed.Index, removed.Generation), "Churn release failed.");
                live.RemoveAt(at); dead.Add(removed);
            }
            Require(store.Count == live.Count, "Count diverged during chunk churn.");
        }
        foreach (var stale in dead) Require(!store.Contains(stale.Index, stale.Generation), "A stale handle was revived during churn.");
        foreach (var current in live) Require(store.Contains(current.Index, current.Generation), "A live handle was lost during churn.");
        Console.WriteLine("PASS: 10,000-resource resize/isolation; harvest and growth state; release/replant invalidation; reload; 50,000 deterministic churn operations.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
