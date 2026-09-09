using ProjectF.Conveyors;

int checks = 0;
void Require(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
const long tick = BeltSimulationJob.TickUnits;

BeltSimulationBuffers Create(int count, int groups = 1, int splitters = 0)
{
    var buffers = new BeltSimulationBuffers(count, groups, splitters, splitters);
    int per = count / groups;
    for (int g = 0; g < groups; g++)
        buffers.Groups[g] = new BeltGroupRange { Start = g * per, Count = per, MaxWaves = 8 };
    for (int i = 0; i < count; i++)
    {
        buffers.Lanes[i] = BeltLaneState.Empty;
        buffers.Topology[i] = new BeltLaneTopology { Target = -1, Alternate = -1, Splitter = -1, Duration = tick * 3 };
        buffers.MergeCursor[i] = (i / per) * per;
    }
    return buffers;
}
void Edge(BeltSimulationBuffers b, int source, int target, long duration = tick * 3)
{
    var route = b.Topology[source]; route.Target = target; route.Duration = duration; b.Topology[source] = route;
}
void Put(BeltSimulationBuffers b, int lane, int id)
{
    b.Lanes[lane] = new BeltLaneState { ItemId = id, Origin = -1, GateBits = 8 };
    for (int g = 0; g < b.Groups.Length; g++) b.GroupStates[g] = default;
}
void Step(BeltSimulationBuffers b, bool parallel = false, bool reverse = false)
{
    var job = b.Job;
    if (parallel) Parallel.For(0, b.Groups.Length, job.Execute);
    else for (int i = 0; i < b.Groups.Length; i++) job.Execute(reverse ? b.Groups.Length - i - 1 : i);
}
int Count(BeltSimulationBuffers b)
{
    int count = 0; for (int i = 0; i < b.Lanes.Length; i++) if (b.Lanes[i].ItemId >= 0) count++; return count;
}
string State(BeltSimulationBuffers b)
{
    var builder = new System.Text.StringBuilder();
    for (int i = 0; i < b.Lanes.Length; i++)
    {
        var s = b.Lanes[i]; builder.Append($"{s.ItemId}:{s.Remaining}:{s.Duration}:{s.Origin}:{s.GateBits}:{b.MergeCursor[i]}|");
    }
    for (int i = 0; i < b.Splitters.Length; i++)
    {
        var s = b.Splitters[i]; builder.Append($"S{s.NextInput}:{s.NextOutput}:{s.WheelMask}|");
    }
    return builder.ToString();
}

using (var b = Create(4))
{
    Edge(b, 0, 1); Edge(b, 1, 2); Edge(b, 2, 3);
    Put(b, 0, 7); Step(b);
    Require(b.Lanes[1].ItemId == 7 && b.Lanes[1].Remaining == 2 * tick, "first fixed tick advances one tick of travel");
    Step(b); Require(b.Lanes[1].Remaining == tick, "integer travel countdown");
    Step(b); Require(b.Lanes[2].ItemId == 7 && b.Lanes[2].Remaining == 3 * tick, "arrival transfers with no duplicated frame delay");
    for (int i = 0; i < 20; i++) Step(b);
    Require(Count(b) == 1 && b.Lanes[3].ItemId == 7 && b.GroupStates[0].Sleeping != 0, "blocked endpoint sleeps and conserves items");
    string stable = State(b); for (int i = 0; i < 100; i++) Step(b);
    Require(State(b) == stable, "sleep does not accumulate movement debt");
}
using (var b = Create(4))
{
    for (int i = 0; i < 4; i++) { Edge(b, i, (i + 1) % 4, tick); Put(b, i, i + 10); }
    Step(b);
    Require(Count(b) == 4, "full loop preserves all items");
    Require(b.Lanes[0].ItemId != 10, "full loop rotates instead of deadlocking");
    for (int i = 0; i < 100; i++) { Step(b); Require(Count(b) == 4, "closed-loop conservation"); }
}
using (var b = Create(3))
{
    Edge(b, 0, 2); Edge(b, 1, 2);
    Put(b, 0, 10); Put(b, 1, 20); Step(b);
    int first = b.Lanes[2].ItemId;
    Require(first == 10 && Count(b) == 2, "merge has one deterministic winner");
    Put(b, 2, -1); Put(b, 0, 10); Step(b);
    Require(b.Lanes[2].ItemId == 20, "merge cursor gives waiting input its turn");
}
using (var b = Create(2))
{
    Edge(b, 0, 1); Put(b, 0, 4);
    var held = b.Lanes[0]; held.Remaining = held.Duration = tick * 5; held.GateBits = 1; b.Lanes[0] = held;
    for (int i = 0; i < 4; i++) { Step(b); Require(b.Lanes[0].ItemId == 4, "external placement hold uses ticks"); }
    Step(b); Require(b.Lanes[1].ItemId == 4 && (b.Lanes[1].GateBits & 8) != 0, "hold completes and settles at the deterministic tick");
}
using (var b = Create(4, 1, 1))
{
    b.Groups[0] = new BeltGroupRange { Start = 0, Count = 4, SplitterStart = 0, SplitterCount = 1, MaxWaves = 2 };
    b.Splitters[0] = new BeltSplitterState { LeftInput = 0, RightInput = 1, LeftOutput = 2, RightOutput = 3,
        NextOutput = 1, FilterOutput = 1, FilterStart = 0, FilterWords = 1 };
    b.FilterBits[0] = 1UL << 5;
    for (int i = 0; i < 2; i++) b.Topology[i] = new BeltLaneTopology
        { Target = 2, Alternate = 3, Splitter = 0, SplitterInput = i, Duration = tick * 2, AlternateDuration = tick * 2 };
    Put(b, 0, 5); Put(b, 1, 9); Step(b);
    Require(b.Lanes[2].ItemId == 5 && b.Lanes[3].ItemId == 9, "splitter filter routes both inputs inside the job");
    Require(Count(b) == 2, "splitter conserves item identity and count");
}

using (var b = Create(5, 1, 1))
{
    b.Groups[0] = new BeltGroupRange { Count = 5, SplitterCount = 1, MaxWaves = 3 };
    b.Splitters[0] = new BeltSplitterState { LeftInput = 0, RightInput = 1, LeftOutput = 2, RightOutput = 3 };
    for (int i = 0; i < 2; i++) b.Topology[i] = new BeltLaneTopology
        { Target = 2, Alternate = 3, Splitter = 0, Duration = tick * 2, AlternateDuration = tick * 2 };
    Edge(b, 2, 4); Put(b, 2, 20); Put(b, 4, 40); Put(b, 0, 10);
    Step(b);
    Require(b.Lanes[3].ItemId == 10 && Count(b) == 3, "splitter retries the free output when the preferred output's downstream chain is blocked");
}
using (var b = Create(2))
{
    Edge(b, 0, 1); Put(b, 0, 4); Step(b);
    var route = b.Topology[1]; route.Paused = 1; b.Topology[1] = route;
    long remaining = b.Lanes[1].Remaining;
    Step(b); Step(b);
    Require(b.Lanes[1].Remaining == remaining, "zero belt speed freezes in-flight travel");
}

// Real concurrent group execution must exactly match both serial traversal orders.
using (var serial = Create(512, 16, 16))
using (var parallel = Create(512, 16, 16))
using (var reverse = Create(512, 16, 16))
{
    var random = new Random(14731);
    for (int i = 0; i < 512; i++)
    {
        int start = (i / 32) * 32;
        int target = start + random.Next(32);
        long duration = tick / 2 + random.Next(1, 6) * tick;
        foreach (var b in new[] { serial, parallel, reverse })
        {
            Edge(b, i, target, duration);
            if (i % 3 != 0) Put(b, i, i);
        }
    }
    for (int g = 0; g < 16; g++)
        foreach (var b in new[] { serial, parallel, reverse })
        {
            int start = g * 32;
            var range = b.Groups[g]; range.SplitterStart = g; range.SplitterCount = 1; b.Groups[g] = range;
            b.Splitters[g] = new BeltSplitterState { LeftInput = start, RightInput = start + 1, LeftOutput = start + 2,
                RightOutput = start + 3, NextInput = g & 1, FilterOutput = g % 3, FilterStart = g, FilterWords = 1 };
            b.FilterBits[g] = 0x5555555555555555UL;
            for (int input = 0; input < 2; input++) b.Topology[start + input] = new BeltLaneTopology
                { Target = start + 2, Alternate = start + 3, Splitter = g, Duration = tick * 2, AlternateDuration = tick * 2 };
        }
    int expected = Count(serial);
    for (int step = 0; step < 800; step++)
    {
        if (step % 7 == 0)
        {
            int slot = random.Next(512);
            if (serial.Lanes[slot].ItemId < 0) { expected++; foreach (var b in new[] { serial, parallel, reverse }) Put(b, slot, 10000 + step); }
            else { expected--; foreach (var b in new[] { serial, parallel, reverse }) Put(b, slot, -1); }
        }
        Step(serial); Step(parallel, true); Step(reverse, false, true);
        Require(State(serial) == State(parallel) && State(serial) == State(reverse), "worker order must not affect any state bit");
        Require(Count(serial) == expected, "random merges/cycles/commands conserve items");
    }
}

Require(BeltSimulationMath.Duration(0.5f, 1f) == tick * 30, "canonical travel duration");
Require(BeltSimulationMath.QuantizePositive(float.NaN, 1000) == 0, "invalid config is inert");
Require(BeltSimulationMath.QuantizePositive(0.5f, 1000) == 500, "exact quantization");
var snapshot = new BeltSimulationSnapshot { Tick = 123456789 };
snapshot.Lanes.Add(new BeltSavedLane { X = -12, Y = 19, Lane = 3, OriginLane = 1, OriginX = 45,
    CursorX = -6, CursorLane = 2, State = new BeltLaneState { ItemId = 9, Remaining = 217, Duration = 65539, GateBits = 31, ExitRadius = 3.1f, Origin = -1 } });
using (var stream = new MemoryStream())
{
    using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) BeltSimulationSnapshot.Write(writer, snapshot);
    byte[] first = stream.ToArray(); stream.Position = 0;
    using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
    var restored = BeltSimulationSnapshot.Read(reader);
    Require(restored.Tick == snapshot.Tick && restored.Lanes[0].State.Remaining == 217 && restored.Lanes[0].CursorLane == 2, "checkpoint preserves exact clock/progress/arbitration");
    using var output = new MemoryStream(); using var writer2 = new BinaryWriter(output);
    BeltSimulationSnapshot.Write(writer2, restored);
    Require(first.SequenceEqual(output.ToArray()), "checkpoint serialization is bit exact");
}
using (var host = new TerrainGenerator())
{
    var a = new Block(0); var b = new Block(1); var c = new Block(2);
    a.Edges[0].Add((b, 0)); b.Edges[0].Add((c, 0));
    host.Add(c); host.Add(a); host.Add(b); // Deliberately noncanonical scene creation order.
    host.Frame(0); host.Put(a, 0, 11);
    Require(a.Items[0].ItemId == 11 && host.Read(a).ItemId < 0, "insertion reserves the facade before changing native state");
    host.StepBeltSimulation();
    Require(host.Read(b).ItemId == 11 && a.Items[0].ItemId < 0 && b.Items[0].ItemId == 11, "tick publishes committed item movement");
    long remaining = host.Read(b).Remaining;
    host.Dirty(); host.Frame(0);
    Require(host.Read(b).Remaining == remaining && host.Read(b).ItemId == 11, "topology rebuild retains exact progress");
    host.Put(b, 0, -1); host.StepBeltSimulation();
    Require(host.Read(b).ItemId < 0 && host.Read(c).ItemId < 0, "queued removal cannot move a ghost item downstream");
    var d = new Block(3); c.Edges[0].Add((d, 0)); host.Add(d); host.Put(d, 0, 77);
    host.StepBeltSimulation();
    Require(host.Read(d).ItemId == 77, "pending writes to newly baked lanes survive the old-buffer flush");
    int callbacks = 0;
    host.QueueBeltPlacementCompletion(() => callbacks++, 3f / 60f);
    host.StepBeltSimulation(); host.StepBeltSimulation();
    Require(callbacks == 0, "placement callback waits for fixed ticks");
    host.StepBeltSimulation(); host.StepBeltSimulation();
    Require(callbacks == 1, "placement callback fires once");
    host.Remove(d); host.StepBeltSimulation();
    Require(host.BeltJobLaneCount == 3, "removal releases the native lane at rebuild");
}

using (var host = new TerrainGenerator())
{
    var a = new Block(-5); var b = new Block(-4); var c = new Block(1); var d = new Block(2);
    a.Edges[0].Add((b, 0)); c.Edges[0].Add((d, 0));
    foreach (var block in new[] { d, c, b, a }) host.Add(block);
    host.Put(a, 0, 6); host.Put(c, 0, 7); host.StepBeltSimulation();
    long beforeB = host.Read(b).Remaining, beforeD = host.Read(d).Remaining;
    Require(host.BeltJobGroupCount == 2, "unconnected lines bake into two groups");
    b.Edges[0].Add((c, 0)); host.Dirty(); host.Frame(0);
    Require(host.BeltJobGroupCount == 1 && host.Read(b).Remaining == beforeB && host.Read(d).Remaining == beforeD,
        "joining groups preserves both moving items");
    b.Edges[0].Clear(); host.Dirty(); host.Frame(0);
    Require(host.BeltJobGroupCount == 2 && host.Read(b).ItemId == 6 && host.Read(d).ItemId == 7,
        "splitting a group remaps indices without losing or duplicating items");
}

using (var original = new TerrainGenerator())
using (var restored = new TerrainGenerator())
{
    var first = Enumerable.Range(0, 8).Select(i => new Block(i)).ToArray();
    var second = Enumerable.Range(0, 8).Select(i => new Block(i)).ToArray();
    for (int i = 0; i < 8; i++)
    {
        first[i].Edges[0].Add((first[(i + 1) % 8], 0));
        second[i].Edges[0].Add((second[(i + 1) % 8], 0));
        original.Add(first[i]); restored.Add(second[7 - i]);
        if (i % 2 == 0) original.Put(first[i], 0, i + 10);
    }
    for (int i = 0; i < 7; i++) original.StepBeltSimulation();
    var clockAndEmpty = original.CaptureBeltSimulationSnapshot();
    for (int i = 0; i < 8; i++) if (original.Read(first[i]).ItemId >= 0)
    {
        BeltSavedLane saved = original.CaptureBeltJobLane(first[i], 0);
        // Same ordering as map/conveyor item restore before global clock restore.
        restored.Put(second[i], 0, saved.State.ItemId);
        restored.QueueBeltJobRestore(second[i], 0, saved);
    }
    restored.RestoreBeltSimulationSnapshot(clockAndEmpty);
    Require(original.Committed() == restored.Committed() && original.ComputeBeltSimulationChecksum() == restored.ComputeBeltSimulationChecksum(),
        "map lane checkpoints plus empty-lane clock snapshot restore the complete state and checksum");
    for (int i = 0; i < 200; i++)
    {
        original.StepBeltSimulation(); restored.StepBeltSimulation();
        Require(original.Committed() == restored.Committed(), "loaded simulation continues bit identically");
    }
}
using (var fast = new TerrainGenerator())
using (var slow = new TerrainGenerator())
{
    var a = new Block(0); var b = new Block(0);
    fast.Add(a); slow.Add(b); fast.Put(a, 0, 5, tick * 100); slow.Put(b, 0, 5, tick * 100);
    for (int i = 0; i < 60; i++) fast.Frame(1f / 60f);
    for (int i = 0; i < 10; i++) slow.Frame(0.1f);
    Require(fast.Committed() == slow.Committed(), "render frame rate does not change the state after the same number of fixed ticks");
}
Console.WriteLine($"PASS: {checks} deterministic belt kernel and host checks; serial/reverse/parallel states match.");
