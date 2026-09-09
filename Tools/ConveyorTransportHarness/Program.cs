using System;
using System.Collections.Generic;
using ProjectF.Conveyors;

static class Program
{
    static int checks;
    static void Check(bool pass, string message) { checks++; if (!pass) throw new Exception(message); }
    static void Near(double a, double b, string context) => Check(Math.Abs(a - b) < 0.000001, $"{context}: {a} / {b}");
    static void Main()
    {
        Differential();
        EntranceReservations();
        Scaling();
        Console.WriteLine($"PASS {checks:N0} transport-store assertions.");
    }
    static void EntranceReservations()
    {
        var store = new ConveyorTransportStore<int>(4, 3);
        Check(store.TryInsert(-1, 10), "first entrance item");
        store.Advance(1);
        Check(!store.TryGetLane(0, out _, out _), "exact arrival reserves the next slot without an extra frame");
        Check(store.TryInsert(-1, 11), "next input accepted at exact spacing");
        Check(store.TryGetLane(0, out int id, out _) && id == 11, "new entry is queryable");
        Check(store.TryGetLane(1, out id, out _) && id == 10, "previous entry is queryable at the same instant");
        for (int i = 0; i < 10; i++) { store.Advance(4); store.RemoveLane(3, out _); store.TryInsert(-1, i); }
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { store.Advance(4); store.RemoveLane(3, out _); store.TryInsert(-1, i); }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "warm insertion/removal/compaction reuses storage");
    }
    static void Differential()
    {
        var random = new Random(91021);
        for (int trial = 0; trial < 100; trial++)
        {
            int end = random.Next(8, 200);
            var actual = new ConveyorTransportStore<int>(end + 1, end);
            var expected = new List<(double p, int id)>();
            for (int tick = 0; tick < 500; tick++)
            {
                double movement = random.NextDouble() * 3;
                actual.Advance(movement);
                double limit = end;
                for (int i = expected.Count - 1; i >= 0; i--)
                {
                    var item = expected[i];
                    item.p = Math.Min(item.p + movement, limit);
                    expected[i] = item; limit = item.p - 1;
                }
                int lane = random.Next(end + 1);
                int index = -1;
                for (int i = expected.Count - 1, nextSlot = end + 1; i >= 0; i--)
                {
                    int reserved = Math.Max(0, Math.Min((int)Math.Floor(expected[i].p + 1e-7) + 1, nextSlot - 1));
                    if (reserved == lane) index = i;
                    nextSlot = reserved;
                }
                bool found = actual.TryGetLane(lane, out int id, out double position);
                Check(found == (index >= 0), "lane query");
                if (found) { Check(id == expected[index].id, "lane identity"); Near(position, expected[index].p, "lane position"); }
                if (random.Next(3) == 0)
                {
                    Check(actual.RemoveLane(lane, out id) == (index >= 0), "removal");
                    if (index >= 0) { Check(id == expected[index].id, "removed ID"); expected.RemoveAt(index); }
                }
                double insert = random.Next(4) == 0 ? -1 : random.Next(end + 1);
                bool valid = expected.Count < end + 1;
                foreach (var item in expected)
                    valid &= Math.Abs(item.p - insert) >= 1 - 1e-7;
                int newId = trial * 1000 + tick;
                Check(actual.TryInsert(insert, newId) == valid, "insertion acceptance");
                if (valid) { expected.Add((insert, newId)); expected.Sort((a, b) => a.p.CompareTo(b.p)); }
                Check(actual.Count == expected.Count, "conservation");
                for (int i = 0; i < expected.Count; i++)
                {
                    Check(actual.TryGetAt(i, out id, out position), "rank exists");
                    Check(id == expected[i].id, "item order"); Near(position, expected[i].p, "reference movement");
                }
            }
        }
    }
    static void Scaling()
    {
        foreach (int count in new[] { 100, 1000, 10000, 100000 })
        {
            var store = new ConveyorTransportStore<int>(count + 100, count + 100);
            for (int i = 0; i < count; i++) Check(store.TryInsert(i, i), "dense import");
            store.Advance(0.01);
            long beforeVisits = store.NodeVisits, beforeMoves = store.CommonMoves;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 100; frame++) store.Advance(0.01);
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(store.NodeVisits == beforeVisits, "free flow must not visit item nodes");
            Check(store.CommonMoves - beforeMoves == 100, "one common movement per tick");
            Check(allocated == 0, "warm free flow GC");
            store.Advance(count + 1000);
            beforeVisits = store.NodeVisits;
            for (int frame = 0; frame < 100; frame++) Check(!store.Advance(0.1), "packed line remains asleep");
            Check(store.NodeVisits == beforeVisits, "packed idle node visits");
            int middle = count / 2 + 101;
            Check(store.RemoveLane(middle, out _), "middle pickup");
            beforeVisits = store.NodeVisits;
            store.Advance(1);
            long compressionVisits = store.NodeVisits - beforeVisits;
            Check(compressionVisits < 512, "hole closes without a full item loop");
            Check(store.Count == count - 1, "pickup conservation");
            Console.WriteLine($"{count:N0} moving items: 100 ticks = 100 common shifts / 0 item visits / {allocated} B; middle-pickup compression = {compressionVisits} tree visits.");
        }
    }
}
