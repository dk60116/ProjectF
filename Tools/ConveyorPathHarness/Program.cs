using System;
using ProjectF.Conveyors;
using UnityEngine;

internal static class Program
{
    private static void Main()
    {
        ConveyorSideBarrierChecks.Run();
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        int cases = 0;
        foreach (Vector2Int flow in directions)
        {
            Require(!ConveyorSideHandoffPath.IsSideEntry(flow, flow), "straight input must remain straight");
            Require(!ConveyorSideHandoffPath.IsSideEntry(-flow, flow), "opposed input is not a side entry");
            Require(!ConveyorSideHandoffPath.IsSideEntry(Vector2Int.zero, flow), "internal slot move is not a side entry");
            foreach (int side in new[] { -1, 1 })
            {
                Vector2Int incoming = new Vector2Int(flow.y, -flow.x) * side;
                Require(ConveyorSideHandoffPath.IsSideEntry(incoming, flow), "both sides must turn");
                Require(!ConveyorSideHandoffPath.IsSideEntry(incoming * 2, flow), "bridge spans must not be classified as adjacent inputs");
                Vector3 axis = new Vector3(flow.x, 0f, flow.y);
                Vector3 entryAxis = new Vector3(incoming.x, 0f, incoming.y);
                ValidateApproachSpacing(axis, entryAxis, flow, incoming);
                foreach (float offset in new[] { -0.1f, 0f, 0.1f })
                {
                    Vector3 center = new Vector3(12f, 0.2f, -8f);
                    Vector3 start = center - entryAxis * 0.75f + axis * offset;
                    Vector3 frontSlot = center + axis * 0.25f;
                    Vector3 turn = ConveyorSideHandoffPath.GetTurnPosition(start, frontSlot, flow);
                    Vector3 before = turn - start;
                    Vector3 after = frontSlot - turn;
                    Require(Math.Abs(Vector3.Dot(before, axis)) < 0.00001f, "incoming item must not drift along the receiving belt");
                    Require(Math.Abs(Vector3.Dot(after, entryAxis)) < 0.00001f, "outgoing item must stay on the receiving centerline");
                    Require(Vector3.Dot(before, entryAxis) > 0f && Vector3.Dot(after, axis) > 0f, "neither segment may reverse");
                    Require(Math.Abs(Vector3.Dot(before, after)) < 0.00001f, "turn must be exactly perpendicular");
                    Require(Math.Abs(before.magnitude + after.magnitude - (1f - offset)) < 0.00001f, "timing must use total traveled distance");
                    Require(turn.y == center.y, "flat handoff must preserve height");
                    cases++;
                }
            }
        }
        Console.WriteLine($"Passed {cases} side-handoff paths and 8 approach-slot pipelines: full-rate headway, blocked exit/restart, four rotations and both sides; straight/opposed/internal/bridge exclusions.");
        BeltTopUvChecks.Run();
    }

    private static void ValidateApproachSpacing(Vector3 flowAxis, Vector3 entryAxis, Vector2Int flow, Vector2Int incoming)
    {
        const float spacing = 0.5f;
        Vector3 start = -entryAxis * 0.75f;
        Vector3 target = flowAxis * 0.25f;
        Vector3 approach = ConveyorSideHandoffPath.GetApproachPosition(start, incoming, spacing);
        Vector3 turn = ConveyorSideHandoffPath.GetTurnPosition(approach, target, flow);
        float entryLength = Vector3.Distance(start, approach);
        float mergeLength = Vector3.Distance(approach, turn) + Vector3.Distance(turn, target);
        Require(Math.Abs(entryLength - spacing) < 0.00001f
            && Math.Abs(mergeLength - spacing) < 0.00001f, "the long merge must be divided into normal-spacing reservations");

        foreach (float speed in new[] { 0.5f, 1f, 2f })
        {
            // Two occupied reservation slots, supplied by a compressed input.
            // Drain downstream first, then refill the freed slot in the same tick.
            float interval = spacing / speed;
            const float step = 1f / 120f;
            foreach (float blockedUntil in new[] { 0f, 3f })
            {
                ConveyorMotionTiming entry = ConveyorMotionTiming.FromPath(0f, entryLength, speed);
                ConveyorMotionTiming merge = default;
                bool mergeOccupied = false;
                float previousDeparture = -1f;
                int departed = 0;
                for (int frame = 0; frame < 2400 && departed < 8; frame++)
                {
                    float now = frame * step;
                    if (mergeOccupied && merge.Evaluate(now) >= 1f && now >= blockedUntil)
                    {
                        if (previousDeparture >= 0f)
                        {
                            float headway = now - previousDeparture;
                            Require(headway >= interval - step && headway <= interval + 2f * step,
                                "a queued side input must retain normal headway after the exit opens");
                        }
                        previousDeparture = now;
                        mergeOccupied = false;
                        departed++;
                    }
                    if (!mergeOccupied && entry.Evaluate(now) >= 1f)
                    {
                        merge = ConveyorMotionTiming.FromPath(now, mergeLength, speed);
                        mergeOccupied = true;
                        entry = ConveyorMotionTiming.FromPath(now, entryLength, speed);
                    }
                }
                Require(departed == 8, "all queued items must drain after the exit opens");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
