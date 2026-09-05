using System;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Require(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static void Main()
    {
        foreach (Vector2Int across in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
        {
            var world = new TerrainGenerator();
            Vector2Int origin = new Vector2Int(-13, 24);
            var belt = new Spliterbelt(origin, origin + across);
            var left = new Block(world, origin, belt);
            // Only the anchor maps the object; the second cell resolves through coverage.
            var right = new Block(world, origin + across, null);

            for (int i = 0; i < 100; i++)
            {
                left.Input = 1000 + i * 2; right.Input = left.Input + 1;
                Require(!right.Probe(out _), "preferred input must win regardless of block tick order");
                Require(left.Probe(out Block preview) && preview == right, "left input crosses to right");
                Require(left.Probe(out Block repeated) && repeated == preview, "queries must not advance arbitration");
                Require(left.Move() && right.Output == 1000 + i * 2, "first input conservation");
                Require(right.Move() && left.Output == 1001 + i * 2, "second input crosses to left");
                left.Output = right.Output = -1;
            }

            // A single connected input still alternates both outputs.
            for (int i = 0; i < 100; i++)
            {
                left.Input = i;
                Require(left.Probe(out Block destination), "single input must not wait for absent input");
                Require(destination == (i % 2 == 0 ? right : left), "single input balanced output order");
                Require(left.Move(), "single input advances");
                left.Output = right.Output = -1;
            }

            for (int blocked = 0; blocked < 2; blocked++)
            {
                Block closed = blocked == 0 ? left : right;
                Block open = blocked == 0 ? right : left;
                closed.Output = 77;
                for (int i = 0; i < 30; i++)
                {
                    left.Input = i * 2; right.Input = i * 2 + 1;
                    Require(left.Move() || right.Move(), "open output must continue when other output blocked");
                    Require(open.Output >= 0 && closed.Output == 77, "blocked output cannot be overwritten");
                    open.Output = -1;
                    Require(left.Move() || right.Move(), "both inputs must get a turn on the remaining output");
                    open.Output = -1;
                }
                closed.Output = -1;
            }

            left.Output = 7; right.Output = 8; left.Input = 9;
            Require(!left.Move() && left.Input == 9, "full outputs preserve the waiting item");
            right.Output = -1; // Robot arm picks an output item.
            right.WakeSplitterInputs();
            Require(left.WakeCount > 0 && right.WakeCount > 0 && left.ClearCount > 0 && right.ClearCount > 0,
                "vacancy wakes both inputs and clears stale failures");
            Require(left.Move() && right.Output == 9, "robot-arm vacancy immediately permits the other channel");
            left.Input = right.Input = left.Output = right.Output = -1;

            foreach (Spliterbelt.FilterOutput mode in new[] { Spliterbelt.FilterOutput.Left, Spliterbelt.FilterOutput.Right })
            {
                belt.SetItemFilterEnabled(42, 128, true); belt.SetFilterOutput(mode);
                Block selected = mode == Spliterbelt.FilterOutput.Left ? left : right;
                Block other = selected == left ? right : left;
                for (int i = 0; i < 30; i++)
                {
                    left.Input = 42; right.Input = 43;
                    Require(left.Move() || right.Move(), "filter arbitration accepts eligible input");
                    Require(left.Move() || right.Move(), "second filtered input advances");
                    Require(selected.Output == 42 && other.Output == 43, "selected and other items stay separated");
                    left.Output = right.Output = -1;
                }
                selected.Output = 42; left.Input = 42; right.Input = 43;
                Require(!left.Move() && left.Input == 42, "filtered item cannot leak into unfiltered output");
                Require(right.Move() && other.Output == 43, "blocked filtered input cannot starve eligible input");
                left.Input = right.Input = left.Output = right.Output = -1;
            }

            left.Input = 42; left.Ready = false;
            Require(!left.Move(), "robot-arm insertion animation must finish before routing");
            left.Ready = true; left.MovedThisFrame = true;
            Require(!left.Move(), "same-frame item must not move twice");
            left.MovedThisFrame = false;
            var saved = belt.CaptureSplitterState();
            var copy = saved.Clone(); saved.nextInput = 100;
            Require(copy.nextInput != saved.nextInput, "save state clone is independent");
            belt.ApplySplitterState(copy);
            Require((int)belt.SelectedFilterOutput == copy.filterOutput, "filter output survives restoration");
            Require(belt.CaptureSplitterState().nextInput == copy.nextInput, "input priority survives restoration");
            belt.PrepareForPool();
            Require(belt.SelectedFilterOutput == Spliterbelt.FilterOutput.Disabled
                && belt.GetAllowedOutputMask(42) == 3, "pool reuse removes old filter state");
        }
        foreach (Vector2Int across in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
            CheckWheelActivity(across);
        CheckWheelTransitionDelay();
        Console.WriteLine($"PASS: {checks} splitter routing/integration/wheel assertions. No Unity engine or physics execution.");
    }

    private static void CheckWheelTransitionDelay()
    {
        var belt = new Spliterbelt(Vector2Int.zero, Vector2Int.right);
        belt.ApplySplitterState(new Spliterbelt.PersistentState { wheelRotationMask = 0 });
        belt.WheelAnimationTime = 10f;
        belt.CommitTransfer(0, 0);
        Require(belt.GetDisplayedWheelRotationMask(10f) == 0, "straight does not start wheel immediately");
        Require(belt.GetDisplayedWheelRotationMask(10.14f) == 0, "straight waits for configured delay");
        belt.WheelAnimationTime = 10.1f;
        belt.CommitTransfer(1, 1);
        belt.CommitTransfer(0, 0);
        Require(belt.GetDisplayedWheelRotationMask(10.16f) == 1,
            "same-result traffic cannot postpone left start; right has its own delay");
        Require(belt.GetDisplayedWheelRotationMask(10.26f) == 3, "right starts after its independent delay");
        belt.WheelAnimationTime = 11f;
        belt.CommitTransfer(0, 1);
        Require(belt.GetDisplayedWheelRotationMask(11.14f) == 3, "cross keeps wheel running during stop delay");
        Require(belt.GetDisplayedWheelRotationMask(11.16f) == 2, "cross stops source after delay");
        Require(belt.GetDisplayedWheelRotationMask(20f) == 2, "empty wait preserves delayed result");

        belt.WheelAnimationTime = 21f;
        belt.CommitTransfer(0, 0);
        belt.WheelAnimationTime = 21.05f;
        belt.CommitTransfer(0, 1);
        Require(belt.GetDisplayedWheelRotationMask(21.3f) == 2,
            "newer opposite result cancels obsolete pending start");
        belt.WheelAnimationTime = 22f;
        belt.CommitTransfer(1, 0);
        var saved = belt.CaptureSplitterState();
        Require(saved.wheelRotationMask == 0, "save records latest committed target during visual delay");
        belt.ApplySplitterState(saved);
        Require(belt.GetDisplayedWheelRotationMask(22f) == 0, "restore settles target without stale delay");

        belt.SetWheelTransitionDelay(0f);
        belt.WheelAnimationTime = 23f;
        belt.CommitTransfer(0, 0);
        Require(belt.GetDisplayedWheelRotationMask(23f) == 1, "zero delay retains immediate transition option");
        belt.SetWheelTransitionDelay(0.5f);
        belt.WheelAnimationTime = 24f;
        belt.CommitTransfer(1, 1);
        Require(belt.GetDisplayedWheelRotationMask(24.49f) == 1, "custom delay is respected");
        Require(belt.GetDisplayedWheelRotationMask(24.51f) == 3, "custom delay completes");
        belt.WheelAnimationTime = 25f;
        belt.CommitTransfer(0, 1);
        belt.PrepareForPool();
        Require(belt.GetDisplayedWheelRotationMask(26f) == 0, "pool reuse discards pending transitions and stops both wheels");
    }

    private static void CheckWheelActivity(Vector2Int across)
    {
        var world = new TerrainGenerator();
        var belt = new Spliterbelt(Vector2Int.zero, across);
        var left = new Block(world, Vector2Int.zero, belt);
        var right = new Block(world, across, null);
        Require(belt.WheelRotationMask == 0, "fresh empty splitter starts both wheels stopped");
        Require(belt.GetDisplayedWheelRotationMask(0f) == 0, "first frame displays both wheels stopped");
        for (int sourceChannel = 0; sourceChannel < 2; sourceChannel++)
        {
            Block source = sourceChannel == 0 ? left : right;
            Block other = sourceChannel == 0 ? right : left;
            int sourceBit = 1 << sourceChannel;
            int otherBit = 1 << (1 - sourceChannel);
            left.Input = right.Input = left.Output = right.Output = -1;
            belt.ApplySplitterState(new Spliterbelt.PersistentState
                { nextInput = sourceChannel, nextOutput = sourceChannel, wheelRotationMask = otherBit });
            source.Input = 42;
            Require(source.Probe(out Block straight) && straight == source, "straight route selected");
            Require(belt.WheelRotationMask == otherBit, "planning must not change either wheel");
            Require(source.Move(), "straight transfer committed");
            Require(belt.WheelRotationMask == 3, "straight starts source wheel immediately and preserves other wheel");
            source.Output = -1;
            Require(belt.WheelRotationMask == 3, "empty wait keeps straight-result wheel running");
            source.Input = 43; source.Ready = false;
            Require(!source.Move() && belt.WheelRotationMask == 3, "unready queued item cannot stop wheel");
            source.Ready = true; other.OutputReserved = true; source.OutputReserved = true;
            Require(!source.Move() && belt.WheelRotationMask == 3, "blocked attempt cannot stop wheel");
            other.OutputReserved = source.OutputReserved = false;
            Require(source.Probe(out Block cross) && cross == other, "next item crosses to other channel");
            Require(belt.WheelRotationMask == 3, "cross planning does not stop the waiting wheel early");
            Require(source.Move(), "cross transfer committed");
            Require(belt.WheelRotationMask == otherBit, "cross stops source wheel immediately without waiting for animation");
            other.Output = -1;
            Require(belt.WheelRotationMask == otherBit, "empty wait after cross stays stopped");
            var captured = belt.CaptureSplitterState().Clone();
            belt.PrepareForPool();
            Require(belt.WheelRotationMask == 0, "pool restores both wheels to stopped state");
            belt.ApplySplitterState(captured);
            Require(belt.WheelRotationMask == otherBit, "restoring splitter preserves both wheel results");

            // Filtering and fallback affect the actual route, not the wheel's update rule.
            belt.SetItemFilterEnabled(42, 64, true);
            belt.SetFilterOutput(sourceChannel == 0 ? Spliterbelt.FilterOutput.Left : Spliterbelt.FilterOutput.Right);
            source.Input = 42;
            Require(source.Move() && (belt.WheelRotationMask & sourceBit) != 0, "filtered straight starts source wheel");
            source.Output = -1;
            belt.SetFilterOutput(sourceChannel == 0 ? Spliterbelt.FilterOutput.Right : Spliterbelt.FilterOutput.Left);
            source.Input = 42;
            Require(source.Move() && (belt.WheelRotationMask & sourceBit) == 0, "filtered cross stops source wheel");
            other.Output = 99;
            belt.SetFilterOutput(Spliterbelt.FilterOutput.Disabled);
            source.Input = 44;
            Require(source.Move() && source.Output == 44 && (belt.WheelRotationMask & sourceBit) != 0,
                "actual straight fallback starts source wheel");
        }
    }
}
