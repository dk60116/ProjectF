using ProjectF.Simulation;

static class ActiveTickSetChecks
{
    sealed class Target { internal int Id; internal Target(int id) { Id = id; } }
    internal static void Run(Action<bool, string> require)
    {
        var set = new ActiveTickSet<Target>(Comparer<Target>.Create((a, b) => a.Id.CompareTo(b.Id)));
        var snapshot = new List<Target>();
        var low = new Target(1); var mid = new Target(2); var high = new Target(3);
        set.Add(high); set.Add(low); set.Add(mid);
        require(!set.Add(mid) && set.Count == 3, "duplicate wakes do not duplicate membership");
        set.CopyOrderedTo(snapshot);
        require(snapshot[0] == low && snapshot[1] == mid && snapshot[2] == high, "active snapshot ordered by stable identity");
        set.Remove(low); set.Remove(high);
        require(snapshot.Count == 3, "sleep/removal during Plan cannot mutate the current batch");
        set.CopyOrderedTo(snapshot);
        require(snapshot.Count == 1 && snapshot[0] == mid, "swap removal repairs indices after sorting");
        set.Add(high); set.CopyOrderedTo(snapshot); set.Remove(high); set.Add(low);
        set.CopyOrderedTo(snapshot);
        require(snapshot.Count == 2 && snapshot[0] == low && snapshot[1] == mid, "wake after removal restores deterministic order");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { set.Remove(low); set.Add(low); set.CopyOrderedTo(snapshot); }
        require(GC.GetAllocatedBytesForCurrentThread() == before, "warmed wake/sleep/sort/copy allocates zero bytes");
        set.Clear(); set.CopyOrderedTo(snapshot);
        require(set.Count == 0 && snapshot.Count == 0 && !set.Remove(mid), "world clear releases active membership");
    }
}
