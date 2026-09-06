using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

static class Time { public static float time; public static int frameCount; }
readonly record struct BlockHandle(int Id);
sealed class PortableObject { public bool WasMovedByConveyorThisFrame; }
static class MapObjectTickProfiler {
    public static int Attempts, Successes;
    public static void AddBeltStraightMoveAttempt(bool moved) { Attempts++; if (moved) Successes++; }
    public static void Reset() { Attempts = Successes = 0; }
}
abstract partial class Block {
    public const float ConveyorContinuousMotionEpsilon = 0.0001f;
    public readonly List<int> conveyorItemIds = new() { -1, -1, -1, -1 };
    public readonly List<int> conveyorItemMoveFrames = new() { -1, -1, -1, -1 };
    public readonly List<ConveyorDataMotionState> conveyorItemMotionStates = new() { default, default, default, default };
    public readonly List<PortableObject> conveyorStack = new() { null, null, null, null };
    public readonly List<ConveyorPickupGateState> Gates = new() { default, default, default, default };
    public readonly float[] HeldUntil = new float[4];
    public readonly int[] Versions = new int[4];
    public readonly bool[] Valid = { true, false, true, false };
    public List<string> Trace;
    public string Name;
    public int ValidityReads, ReadinessReads, DirtyCalls;
    public bool StructureValid = true, HasVia, GpuCapable = true;
    public Vector3 Position, Via;
    public float Speed = 1f, UncachedPath = 0.5f;
    public Action<Block, int> OnVacated;
    public abstract bool Transfer(Block destination, int from, int to, float path, bool uncached);
    public bool IsValidConveyorLaneIndex(int lane) {
        ValidityReads++;
        return lane >= 0 && lane < Valid.Length && Valid[lane];
    }
    public bool CanUseStraightConveyorLineSimulationStructureOnly() => StructureValid;
    public static bool IsConveyorDestinationLaneOccupied(Block destination, int lane) => destination != null && destination.HasConveyorItemAtLane(lane);
    public bool IsConveyorLaneMovementHeld(int lane) { ReadinessReads++; return lane >= 0 && lane < 4 && HeldUntil[lane] > Time.time; }
    public bool IsConveyorObjectReadyToMoveAtLane(int lane, PortableObject item) => true;
    public float GetConveyorSpeed() => Speed;
    public float ResolveConveyorDataMotionDurationPathLength(ConveyorDataMotionState state) => state.pathLength;
    public bool CanUseGpuLinearConveyorItemRendering(ConveyorDataMotionState state) => GpuCapable && !state.hasViaWorldPosition && state.duration > ConveyorContinuousMotionEpsilon;
    public float GetConveyorPathSegmentLength(int from, Block destination, int to, bool corner) => UncachedPath;
    public ConveyorPickupGateState GetConveyorPickupGateStateAtLane(int lane) => Gates[lane];
    public Vector3 GetConveyorItemVisualWorldPosition(int lane) {
        var state = conveyorItemMotionStates[lane];
        return state.active ? Vector3.Lerp(state.startWorldPosition, GetConveyorLaneWorldPosition(lane), EvaluateConveyorDataMotionProgress(EnsureConveyorDataMotionTiming(state))) : GetConveyorLaneWorldPosition(lane);
    }
    public Vector3 GetConveyorLaneWorldPosition(int lane) => Position + new Vector3(lane * 0.01f, 0.2f, 0f);
    public bool TryGetConveyorLinearMoveViaWorldPosition(int from, Block destination, int to, Vector3 start, out Vector3 via) { via = Via; return HasVia; }
    public void ClearConveyorItemAtLane(int lane) {
        Trace.Add($"{Name}:clear:{lane}:{conveyorItemIds[lane]}");
        conveyorItemIds[lane] = -1; conveyorStack[lane] = null; conveyorItemMoveFrames[lane] = -1;
        conveyorItemMotionStates[lane] = default; Gates[lane] = default; HeldUntil[lane] = 0; Versions[lane]++;
        Trace.Add($"{Name}:vacated:{lane}:{conveyorItemIds[lane]}");
        OnVacated?.Invoke(this, lane);
    }
    public void SetConveyorItemAtLane(int lane, int id, PortableObject item, ConveyorPickupGateState gate) {
        Trace.Add($"{Name}:set:{lane}:{id}:{gate.isSettled}");
        conveyorItemIds[lane] = id; conveyorStack[lane] = item; conveyorItemMoveFrames[lane] = -1;
        conveyorItemMotionStates[lane] = default; Gates[lane] = gate; HeldUntil[lane] = 0; Versions[lane]++;
    }
    public void MarkConveyorItemVisualDirty() { DirtyCalls++; Trace.Add($"{Name}:dirty"); }
    public void MarkConveyorItemMovedThisFrame(int lane) { conveyorItemMoveFrames[lane] = Time.frameCount; Trace.Add($"{Name}:moved:{lane}:{Time.frameCount}"); }
    static string F(float value) => BitConverter.SingleToInt32Bits(value).ToString("X8");
    static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
    public string Snapshot() {
        var output = new StringBuilder();
        for (int i = 0; i < 4; i++) {
            var state = conveyorItemMotionStates[i]; var gate = Gates[i];
            output.AppendJoin(':', i, conveyorItemIds[i], conveyorItemMoveFrames[i], Versions[i], conveyorStack[i] != null, F(HeldUntil[i]),
                gate.hasGate, gate.requiresExit, gate.hasExited, F(gate.exitRadius), gate.isSettled, V(gate.dropOrigin), gate.hasOrigin, gate.autoPickupBlocked,
                state.active, state.useGpuLinearRendering, state.useCornerMotion, V(state.startWorldPosition), state.hasViaWorldPosition,
                V(state.viaWorldPosition), state.sourceLaneIndex, state.destinationLaneIndex, F(state.progress), F(state.pathLength), F(state.durationPathLength),
                F(state.startTime), F(state.duration)).Append(';');
        }
        return output.Append("dirty:").Append(DirtyCalls).ToString();
    }
}

static class Checks {
    static int assertions, moves;
    static void Assert(bool test, string message) { assertions++; if (!test) throw new Exception(message); }
    static Block New(bool legacy) => legacy ? new LegacyBlock() : new CurrentBlock();
    sealed class World {
        public Block Source, Destination;
        public readonly List<string> Trace = new();
        public bool Result;
        public int Attempts, Successes;
        public string Snapshot() => $"{Result}:{Attempts}:{Successes}|{Source.Snapshot()}|{Destination?.Snapshot()}|{string.Join(',', Trace)}";
    }
    static World Prepare(bool legacy, int destinationKind, int from, int to, int id, bool occupied, bool materialized, bool held, bool moved, int motion) {
        var world = new World { Source = New(legacy) };
        world.Destination = destinationKind == 0 ? null : destinationKind == 1 ? world.Source : New(legacy);
        world.Source.Name = "source"; world.Source.Trace = world.Trace;
        if (world.Destination != null && world.Destination != world.Source) { world.Destination.Name = "destination"; world.Destination.Trace = world.Trace; world.Destination.Position = Vector3.forward; }
        if (from >= 0 && from < 4) {
            world.Source.conveyorItemIds[from] = id;
            world.Source.conveyorStack[from] = materialized ? new PortableObject() : null;
            world.Source.conveyorItemMoveFrames[from] = moved ? Time.frameCount : Time.frameCount - 1;
            world.Source.HeldUntil[from] = held ? Time.time + 1f : Time.time;
            world.Source.Gates[from] = new ConveyorPickupGateState { hasGate = true, requiresExit = true, hasExited = false, exitRadius = 1.25f, isSettled = false, dropOrigin = new Vector3(3, 2, 1), hasOrigin = true, autoPickupBlocked = true };
            if (motion != 0) {
                float progress = motion switch { 1 => 0.5f, 2 => 0.9998f, 3 => 0.9999f, _ => 1f };
                world.Source.conveyorItemMotionStates[from] = new ConveyorDataMotionState { active = true, destinationLaneIndex = from,
                    startTime = Time.time - progress, duration = 1f, pathLength = 1f, startWorldPosition = Vector3.back };
            }
        }
        if (occupied && world.Destination != null && to >= 0 && to < 4) world.Destination.conveyorItemIds[to] = 777;
        if (world.Destination != null) world.Source.OnVacated = (block, lane) =>
            world.Trace.Add($"wake:{block.conveyorItemIds[lane]}:{(to >= 0 && to < 4 ? world.Destination.conveyorItemIds[to] : -999)}");
        return world;
    }
    static void Run(World world, int from, int to, float path, bool uncached) {
        MapObjectTickProfiler.Reset();
        world.Result = world.Source.Transfer(world.Destination, from, to, path, uncached);
        world.Attempts = MapObjectTickProfiler.Attempts; world.Successes = MapObjectTickProfiler.Successes;
    }
    static void Compare(World old, World current, int from, int to, float path = 0.5f, bool uncached = false) {
        Run(old, from, to, path, uncached); Run(current, from, to, path, uncached);
        string left = old.Snapshot(), right = current.Snapshot();
        Assert(left == right, $"Transfer mismatch: from={from}, to={to}, uncached={uncached}\nOLD {left}\nNEW {right}");
        Assert(current.Attempts == (current.Result ? 1 : 0), "current callers count only accepted transfers");
        if (current.Result) moves++;
    }
    static void FailureMatrix() {
        foreach (bool uncached in new[] { false, true })
        foreach (int destinationKind in new[] { 0, 1, 2 })
        foreach (int from in new[] { -1, 0, 1, 2, 4 })
        foreach (int to in new[] { -1, 0, 1, 2, 4 })
        foreach (int item in new[] { -1, 0, 13 })
        foreach (bool occupied in new[] { false, true })
        foreach (bool materialized in new[] { false, true })
        foreach (bool held in new[] { false, true })
        foreach (bool moved in new[] { false, true })
        foreach (int motion in new[] { 0, 1, 2, 3, 4 }) {
            var old = Prepare(true, destinationKind, from, to, item, occupied, materialized, held, moved, motion);
            var current = Prepare(false, destinationKind, from, to, item, occupied, materialized, held, moved, motion);
            Compare(old, current, from, to, uncached: uncached);
        }
    }
    static void MotionAndBoundaryCases() {
        foreach (int direction in new[] { 0, 1, 2, 3 })
        foreach (float speed in new[] { 0f, 0.0001f, 0.00011f, 0.25f, 1f, 4f })
        foreach (float path in new[] { 0f, 0.0001f, 0.5f, 2f })
        foreach (bool via in new[] { false, true })
        foreach (bool uncached in new[] { false, true }) {
            var old = Prepare(true, 2, 0, 2, 13, false, false, false, false, 0);
            var current = Prepare(false, 2, 0, 2, 13, false, false, false, false, 0);
            foreach (var world in new[] { old, current }) {
                world.Source.Position = new Vector3(direction * 3, 0.5f, -2);
                var offset = direction switch { 0 => Vector3.forward, 1 => Vector3.right, 2 => Vector3.back, _ => Vector3.left };
                world.Destination.Position = world.Source.Position + offset;
                world.Source.Speed = speed * 0.5f; world.Destination.Speed = speed;
                world.Source.UncachedPath = path; world.Source.HasVia = via;
                world.Source.Via = world.Source.Position + offset * 0.2f + Vector3.up * 0.1f;
            }
            Compare(old, current, 0, 2, path, uncached);
            Assert(current.Trace[0].StartsWith("source:clear") && current.Trace[2] == "wake:-1:-1", "vacancy callback observes source removed before destination assigned");
            Assert(current.Trace[3].StartsWith("destination:set:2:13:True") && current.Trace[4] == "destination:dirty" && current.Trace[5].StartsWith("destination:moved"), "settled pickup gate, motion dirty and move stamp order");
        }
        foreach (bool sourceValid in new[] { false, true })
        foreach (bool destinationValid in new[] { false, true }) {
            var old = Prepare(true, 2, 0, 2, 13, false, false, false, false, 0);
            var current = Prepare(false, 2, 0, 2, 13, false, false, false, false, 0);
            foreach (var world in new[] { old, current }) { world.Source.StructureValid = sourceValid; world.Destination.StructureValid = destinationValid; }
            Compare(old, current, 0, 2, uncached: true);
            Assert(current.Result == (sourceValid && destinationValid), "noncached structure fallback unchanged");
        }
    }
    static void CountReduction() {
        var old = Prepare(true, 2, 0, 2, 13, false, false, false, false, 0);
        var current = Prepare(false, 2, 0, 2, 13, false, false, false, false, 0);
        Compare(old, current, 0, 2);
        Assert(old.Source.ReadinessReads == 2 && current.Source.ReadinessReads == 1, "ready check reduced from twice to once");
        int before = old.Source.ValidityReads + old.Destination.ValidityReads, after = current.Source.ValidityReads + current.Destination.ValidityReads;
        Assert(after < before, "duplicate lane validation reduced");
        Console.WriteLine($"Accepted straight transfer: readiness checks {old.Source.ReadinessReads} -> {current.Source.ReadinessReads}; lane validity checks {before} -> {after}.");
        old.Source.ReadinessReads = current.Source.ReadinessReads = 0;
        Compare(old, current, 0, 2);
        Assert(old.Source.ReadinessReads == 0 && current.Source.ReadinessReads == 0, "occupied rejection still stops before readiness");
    }
    public static int Main() {
        Time.time = 100f; Time.frameCount = 50;
        FailureMatrix(); MotionAndBoundaryCases(); CountReduction();
        Assert(moves > 100, "matrix must include accepted transfers");
        Console.WriteLine($"PASS {assertions:N0} assertions, {moves:N0} accepted transfers compared.");
        Console.WriteLine("Actual old/current transfer methods and readiness/time helpers; ownership notifications and world boundaries are managed doubles. No Unity process launched.");
        return 0;
    }
}
