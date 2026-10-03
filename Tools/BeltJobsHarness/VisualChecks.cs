using ProjectF.Conveyors;
using ProjectF.Rendering;
using UnityEngine;

static class VisualChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new Exception(message); }
        void Near(Vector3 actual, Vector3 expected, string message)
            => Check(Vector3.Distance(actual, expected) < 0.00002f, message);
        var random = new Random(317);
        for (int i = 0; i < 1000; i++)
        {
            Vector3 a = new((float)random.NextDouble()*8-4, 1, 3);
            Vector3 b = new(6, (float)random.NextDouble()*2, -7);
            Vector3 via = i % 3 == 0 ? a : i % 3 == 1 ? b : new Vector3(-3, 4, 2);
            float t = i / 999f;
            Near(BeltItemVisualPath.Line(a, b).Evaluate(t), Vector3.Lerp(a, b, t), "line geometry matches legacy interpolation");
            float first = Vector3.Distance(a, via), second = Vector3.Distance(via, b);
            float distance = (first + second) * t;
            Vector3 oldVia = distance <= first
                ? Vector3.Lerp(a, via, first > 0 ? distance / first : 1)
                : Vector3.Lerp(via, b, second > 0 ? (distance-first) / second : 1);
            Near(BeltItemVisualPath.Through(a, via, b).Evaluate(t), oldVia, "unequal/zero length via legs match legacy distance timing");
            float start = -2.3f, delta = i % 2 == 0 ? MathF.PI/2 : -MathF.PI/2, radius = 0.37f;
            Near(BeltItemVisualPath.Arc(a, start, delta, radius).Evaluate(t),
                a + new Vector3(MathF.Cos(start+delta*t)*radius, 0, MathF.Sin(start+delta*t)*radius), "both corner directions retain arc geometry");
            Vector3 jump = Vector3.Lerp(a,b,t); jump.y += MathF.Sin(t*MathF.PI)*0.35f;
            Near(BeltItemVisualPath.Line(a,b,true).Evaluate(t), jump, "external jump retains existing arc height");
        }
        Near(BeltItemVisualPath.Through(default, default, default).Evaluate(0.5f), default, "degenerate path remains finite");
        using var host = new TerrainGenerator();
        Block source = new(-3), target = new(-2);
        source.Edges[0].Add((target, 0));
        source.Items[0] = new BeltLaneState { ItemId = 8, Origin = -1, GateBits = 8 };
        host.Add(source); host.Add(target); host.Frame(0); host.StepBeltSimulation();
        host.BeginBeltItemRendering();
        Check(host.TryReadBeltJobLaneForRendering(target, 0, out var state), "prepared native lane exists");
        Check(state.Remaining == host.Read(target).Remaining, "prepared read applies group deferred time exactly once");
        var cache = new BeltItemVisualPathCache();
        string before = host.Committed();
        int captures = source.VisualPathCaptures;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            host.TryReadBeltJobLaneForRendering(target, 0, out state);
            var path = host.GetBeltJobVisualPath(target, 0, state, ref cache);
            path.Evaluate(host.GetBeltJobVisualProgress(target,0,state));
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(source.VisualPathCaptures == captures+1, "1000 render frames capture unchanged topology geometry once");
        Check(bytes == 0, "native read/path/progress loop allocates no managed memory after topology bake");
        Check(host.Committed() == before, "rendering does not mutate transport state or clock");
        host.StepBeltSimulation(); host.BeginBeltItemRendering();
        host.TryReadBeltJobLaneForRendering(target,0,out state);
        Check(state.Remaining==BeltSimulationJob.TickUnits, "prepared read includes skipped intermediate tick's deferred time");
        host.Hold(target,0,BeltSimulationJob.TickUnits*8);
        host.BeginBeltItemRendering();
        host.TryReadBeltJobLaneForRendering(target,0,out state);
        Check(state.Remaining == BeltSimulationJob.TickUnits*8, "pending hold overlays native presentation before commit");
        host.Put(target,0,19);
        host.TryReadBeltJobLaneForRendering(target,0,out state);
        Check(state.ItemId == 19 && state.Origin == -1, "pending external replacement overrides native slot");
        var external = host.GetBeltJobVisualPath(target,0,state,ref cache);
        state.StartX = 15; state.GateBits |= 64;
        external = host.GetBeltJobVisualPath(target,0,state,ref cache);
        Check(external.Kind == 4 && external.Start.x == 15, "external position and jump payload remain live despite path cache");
        host.Put(target,0,-1);
        host.TryReadBeltJobLaneForRendering(target,0,out state);
        Check(state.ItemId == -1, "pending removal hides old native item immediately");
        host.StepBeltSimulation(); host.Dirty(); host.Frame(0);
        source.Items[0] = new BeltLaneState { ItemId=20, Origin=-1, GateBits=8 };
        host.Put(source,0,20); host.StepBeltSimulation();
        host.BeginBeltItemRendering(); host.TryReadBeltJobLaneForRendering(target,0,out state);
        host.GetBeltJobVisualPath(target,0,state,ref cache);
        Check(source.VisualPathCaptures == captures+2, "topology rebuild invalidates visible path descriptors");
        target.RuntimeConveyorSpeed=0; host.Dirty(); host.Frame(0);
        host.BeginBeltItemRendering(); host.TryReadBeltJobLaneForRendering(target,0,out state);
        long pausedRemaining=state.Remaining;
        host.StepBeltSimulation(); host.BeginBeltItemRendering(); host.TryReadBeltJobLaneForRendering(target,0,out state);
        Check(state.Remaining==pausedRemaining, "prepared read preserves paused native segment time");
        host.BeltSimulationExternallyClocked=true;
        float p=host.GetBeltJobVisualProgress(target,0,state);
        Check(float.IsFinite(p), "external-clock view remains finite with a paused segment");
        Console.WriteLine($"Native item view: 1000 frames = 1 geometry capture, {bytes} B managed allocation; line/via/corner/jump checks PASS (engine boundaries doubled)");
        return checks;
    }
}
