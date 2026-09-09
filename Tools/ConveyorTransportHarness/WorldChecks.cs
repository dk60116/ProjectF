using System;
using System.Collections.Generic;
using ProjectF.Conveyors;
using UnityEngine;

static partial class WorldChecks
{
    static int checks;
    static void Check(bool pass, string message) { checks++; if (!pass) throw new Exception(message); }
    static void Main()
    {
        foreach (int direction in new[] { 1, -1 })
        {
            Time.time = 0; Time.frameCount = 0;
            var world = new TerrainGenerator(102, direction);
            for (int i = 1; i < 90; i++) world.Blocks[i].Put(0, i);
            world.Tick();
            Check(world.Runs == 1 && world.Items == 89, "active runtime ownership");
            for (int b = 1; b < 101; b++) Check(world.Blocks[b].RawCount == 0 && world.Blocks[b].VisualTracked, "legacy storage empty, rendering registered");
            int initialWrites = world.RawWrites;
            for (int frame = 1; frame <= 10; frame++) { Time.time = frame * 0.02f; Time.frameCount = frame; world.Tick(); }
            Check(world.RawWrites == initialWrites, "internal movement cannot write block storage");
            Check(world.Items == 89, "item conservation during movement");
            var run = world.Blocks[10].ConveyorTransport;
            run.Items.TryGetAt(10, out var before, out double position);
            int slot = run.Items.GetReservedLane(10, position);
            Block pickup = run.Blocks[slot / 2]; int lane = slot % 2;
            Check(pickup.Id(lane) == before.Id, "robot/player lane query follows common displacement");
            pickup.SetGate(lane, new ConveyorPickupGateState { hasGate = true, isSettled = false });
            Check(!pickup.Gate(lane).isSettled, "gate mutation is stored by the line");
            var visual = pickup.Visual(lane);
            Check(Math.Abs(visual.x - run.WorldPosition(position).x) < 0.0001, "render position");
            pickup.Take(lane);
            Check(run.Items.Count == 88 && run.Active, "middle pickup is local, run keeps ownership");
            // Unsupported animation/placement converts once and becomes a boundary.
            world.Blocks[40].Put(0, 9001);
            Check(!run.Active, "unsupported placement exports before mutation");
            Check(world.Total == 89, "release conserves other items");
            Time.time += 2; Time.frameCount++; world.Tick();
            Check(world.Runs == 2, "interaction block splits the transport into owned segments");
            Check(!world.Blocks[40].OwnsConveyorTransport, "interaction boundary stays on legacy backend");
            int count = world.Total;
            var poses = new Dictionary<int, Vector3>();
            foreach (Block block in world.Blocks)
                for (int side = 0; side < 2; side++) if (block.Id(side) >= 0) poses.Add(block.Id(side), block.Visual(side));
            world.Invalidate();
            Check(world.Total == count, "topology release exports without loss");
            foreach (Block block in world.Blocks)
                for (int side = 0; side < 2; side++)
                    if (block.Id(side) >= 0) Check(Vector3.Distance(poses[block.Id(side)], block.Visual(side)) < 0.0001, "export keeps in-flight pose");
        }
        Flow();
        ImportPrecision();
        RuntimeScaling();
        WakeScheduling();
        BoundaryTiming();
        Console.WriteLine($"PASS {checks:N0} ownership/ports/query/render-projection/topology assertions; Unity boundaries are doubles.");
    }
    static void ImportPrecision()
    {
        foreach (float bias in new[] { -0.0003f, -0.1f })
        {
            Time.time = 0; Time.frameCount = 0;
            var world = new TerrainGenerator(12, 1);
            for (int b = 1; b <= 10; b++) { world.Blocks[b].Put(0, b * 2); world.Blocks[b].Put(1, b * 2 + 1); }
            world.Blocks[5].VisualBias = bias;
            int writes = world.RawWrites;
            world.Tick();
            Check(world.Total == 20, "import precision preserves all identities");
            if (bias > -0.001f) Check(world.Runs == 1, "sub-millimetre float gap error does not disable transport");
            else Check(world.Runs == 0 && world.RawWrites == writes, "real overlap retains untouched legacy ownership");
            world.Invalidate();
        }
    }
    static void RuntimeScaling()
    {
        foreach (int length in new[] { 100, 1000, 10000, 100000 })
        {
            Time.time = 0; Time.frameCount = 0;
            var world = new TerrainGenerator(length, 1);
            for (int b = 1; b < length / 2; b++) { world.Blocks[b].Put(0, b * 2); world.Blocks[b].Put(1, b * 2 + 1); }
            world.Tick();
            int writes = world.RawWrites;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 1; frame <= 100; frame++) { Time.time = frame * 0.0001f; Time.frameCount = frame; world.Tick(); }
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Check(world.RawWrites == writes, "runtime 100 ticks perform no interior slot writes");
            Check(world.Runs == 1 && world.LegacyBlocks == 2, "only two legacy ports regardless of line length");
            Check(bytes == 0, "warm transport manager allocation");
            Console.WriteLine($"Runtime {length:N0} blocks / {world.Items:N0} moving items: 100 ticks / 0 interior slot writes / 2 legacy ports / {bytes} B (world doubles).");
            world.Invalidate();
        }
    }
    static void Flow()
    {
        Time.time = 0; Time.frameCount = 0;
        var world = new TerrainGenerator(12, 1);
        world.Tick();
        int supplied = 0, consumed = 0;
        var received = new List<int>();
        for (int frame = 1; frame < 3000; frame++)
        {
            Time.time = frame * 0.025f; Time.frameCount = frame;
            if (supplied < 70 && world.Blocks[0].Id(1) < 0) world.Blocks[0].Put(1, ++supplied);
            world.Tick();
            Block output = world.Blocks[11];
            if ((frame < 300 || frame > 800) && output.Id(0) >= 0 && output.Ready(0))
            {
                received.Add(output.Id(0)); output.Take(0); consumed++;
            }
            Check(world.Total + consumed == supplied, "boundary item conservation");
            Check(world.Runs == 1, "normal ports never release/reimport the run");
            world.Tick(); // duplicate frame must not advance or transfer twice
            Check(world.Total + consumed == supplied, "same-frame conservation");
        }
        Check(consumed == supplied && consumed == 70, "blocked output resumes and drains");
        for (int i = 0; i < received.Count; i++) Check(received[i] == i + 1, "FIFO at boundary");
    }
}

namespace UnityEngine
{
    public static class Time { public static float time; public static int frameCount; }
    public static class Application { public static bool isPlaying = true; }
    public static class Mathf
    {
        public static int Clamp(int x, int lo, int hi) => Math.Max(lo, Math.Min(hi, x));
        public static float Abs(float x) => Math.Abs(x);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y=0, float z=0) { this.x=x; this.y=y; this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a-b).sqrMagnitude);
    }
}
internal struct ConveyorPickupGateState
{
    internal bool hasGate, isSettled;
    internal void MarkSettled() { isSettled = true; }
    internal static ConveyorPickupGateState Settled() => new ConveyorPickupGateState { isSettled = true };
}
internal struct Continuation { internal bool active; }
internal struct ConveyorDataMotionState
{
    internal bool active, useCornerMotion, hasViaWorldPosition;
    internal Continuation cornerContinuation;
    internal Vector3 startWorldPosition;
    internal int destinationLaneIndex;
    internal float pathLength;
    internal float startTime, duration;
}
internal readonly struct BlockHandle { internal readonly int Index; internal BlockHandle(int index) { Index = index; } }
internal sealed class ProxyStore { internal ulong RuntimeProxyVersion; }

public partial class TerrainGenerator
{
    internal readonly Block[] Blocks;
    private readonly List<ConveyorLine> conveyorLines = new List<ConveyorLine>();
    private readonly ProxyStore loadedBlocks = new ProxyStore();
    private class ConveyorLine
    {
        internal int id = 1;
        internal bool simulationCacheValid = true;
        internal List<BlockHandle> blockHandles = new List<BlockHandle>();
        internal int[] frontLaneIndices, backLaneIndices;
        internal float[] withinPathLengths, nextPathLengths;
        internal List<ConveyorTransportRun> transportRuns;
        internal List<int> transportLegacySlots;
        internal ulong transportProxyVersion;
        internal float transportRetryTime;
    }
    internal TerrainGenerator(int length, int direction)
    {
        Blocks = new Block[length];
        var line = new ConveyorLine { frontLaneIndices = new int[length], backLaneIndices = new int[length], withinPathLengths = new float[length], nextPathLengths = new float[length] };
        for (int i=0;i<length;i++)
        {
            Blocks[i] = new Block(i,direction) { World = this }; line.blockHandles.Add(new BlockHandle(i));
            line.frontLaneIndices[i] = 1; line.withinPathLengths[i] = line.nextPathLengths[i] = 0.5f;
            if (i>0) Blocks[i-1].Next = Blocks[i];
        }
        conveyorLines.Add(line);
    }
    internal void Tick() { BeginFrame(); TickOwnedConveyorRuns(); }
    internal void Invalidate() { ReleaseAllConveyorTransport(); }
    internal int Runs => lastTransportRuns;
    internal int Items => lastTransportItems;
    internal int LegacyBlocks => lastTransportLegacyBlocks;
    internal int RawWrites { get { int n=0; foreach(var b in Blocks) n+=b.Writes; return n; } }
    internal int Total { get { int n=0; foreach(var b in Blocks) n+=b.RawCount; foreach(var line in conveyorLines) if(line.transportRuns!=null) foreach(var r in line.transportRuns) if(r.Active) n+=r.Items.Count; return n; } }
    private bool TryResolveConveyorLineBlock(ConveyorLine line, int i, out Block block) { block=Blocks[i]; return true; }
    private bool TryResolveLoadedRuntimeBlock(BlockHandle handle, out Block block) { block=Blocks[handle.Index]; return true; }
    private bool IsLoadedRuntimeBlock(Block block) => block != null && Blocks[block.Index] == block;
    private void ClearStraightConveyorLineRetry(int id) { }
}

public partial class Block
{
    private readonly int index, direction;
    internal Block Next;
    internal int Writes;
    internal bool VisualTracked;
    internal float VisualBias;
    private readonly List<int> ids = new List<int> { -1, -1 };
    private List<int> conveyorItemIds => ids;
    private readonly float[] ready = new float[2];
    private readonly List<float> conveyorItemMovementHoldUntilTimes = new List<float> {0,0};
    private readonly List<ConveyorDataMotionState> conveyorItemMotionStates = new List<ConveyorDataMotionState> {default,default};
    private readonly List<ConveyorPickupGateState> conveyorItemPickupGateStates = new List<ConveyorPickupGateState> {default,default};
    private const float ConveyorContinuousMotionEpsilon = 0.0001f;
    internal float Speed = 2;
    public float RuntimeConveyorSpeed => Speed;
    internal Block(int index,int direction) { this.index=index; this.direction=direction; }
    internal int RawCount => (ids[0]>=0?1:0)+(ids[1]>=0?1:0);
    internal int Id(int lane) => GetConveyorStoredItemIdAtLane(lane);
    internal Vector3 Visual(int lane) => GetConveyorItemVisualWorldPosition(lane);
    internal bool Ready(int lane) => !OwnsConveyorTransport && Time.time >= ready[lane] && !IsConveyorLaneMovementHeld(lane);
    internal void Put(int lane,int id) { SetConveyorItemAtLane(lane,id,null,default); }
    internal void Take(int lane) { ClearConveyorItemAtLane(lane); }
    internal void SetGate(int lane, ConveyorPickupGateState gate) => SetConveyorPickupGateStateAtLane(lane, gate);
    internal ConveyorPickupGateState Gate(int lane) => GetConveyorPickupGateStateAtLane(lane);
    private bool ShouldUseVirtualConveyorItemRendering() => true;
    private bool HasRuntimeBelt2FConveyor() => false;
    public bool CanUseStraightConveyorLineSimulationStructureOnly() => true;
    public bool HasStraightConveyorLineFastPathRuntimeBlocker() => false;
    private bool IsConveyorLaneMovementHeld(int lane) => conveyorItemMovementHoldUntilTimes[lane] > Time.time;
    private Vector3 GetConveyorLaneWorldPosition(int lane) => new Vector3(direction*(index+lane*0.5f));
    private bool IsConveyorStorageLaneIndex(int lane) => lane >= 0 && lane < ids.Count;
    private int GetConveyorItemIdAtLane(int lane) => GetConveyorStoredItemIdAtLane(lane);
    private bool HasConveyorItemAtLane(int lane) => Id(lane) >= 0;
    private object GetConveyorPortableObjectAtLane(int lane) => null;
    private ConveyorDataMotionState EnsureConveyorDataMotionTiming(ConveyorDataMotionState motion) => motion;
    private float GetConveyorDataMotionCompletionTime(ConveyorDataMotionState motion, float now) => motion.startTime + motion.duration;
    private void InvalidateConveyorCanMoveCaches() { }
    private Vector3 GetConveyorItemVisualWorldPosition(int lane)
    {
        if(ReadTransportLane(lane,out _,out double p)) return ConveyorTransport.WorldPosition(p);
        var motion = conveyorItemMotionStates[lane];
        if (motion.active && motion.duration > 0)
        {
            float progress = Math.Clamp((Time.time - motion.startTime) / motion.duration, 0, 1);
            return motion.startWorldPosition + (GetConveyorLaneWorldPosition(lane) - motion.startWorldPosition) * progress;
        }
        if(Time.time>=ready[lane]) return GetConveyorLaneWorldPosition(lane) + new Vector3(direction * VisualBias);
        return GetConveyorLaneWorldPosition(lane) - new Vector3(direction*(ready[lane]-Time.time)*RuntimeConveyorSpeed);
    }
    private void ClearConveyorStorageLaneRaw(int lane)
    {
        if(OwnsConveyorTransport) ConveyorTransport.Remove(TransportSlot(lane));
        IncrementConveyorLaneOccupancyVersion(lane);
        ids[lane]=-1; Writes++; ready[lane]=0; conveyorItemMotionStates[lane]=default;
    }
    private void ClearConveyorItemAtLane(int lane) => ClearConveyorStorageLaneRaw(lane);
    private void SetConveyorItemAtLane(int lane,int id,object portable,ConveyorPickupGateState gate)
    {
        ReleaseConveyorTransport(true); IncrementConveyorLaneOccupancyVersion(lane);
        ids[lane]=id; ready[lane]=Time.time; Writes++; conveyorItemPickupGateStates[lane]=gate;
        conveyorItemMotionStates[lane]=default;
    }
    private void MarkConveyorItemVisualDirty() { }
    public void RefreshConveyorActivityRegistration(bool queueWake=true,bool refreshDebugVisuals=true)
    {
        VisualTracked=OwnsConveyorTransport;
        if (queueWake) World.QueueConveyorWake(this);
    }
    private ConveyorDataMotionState InitializeConveyorDataMotionTiming(ConveyorDataMotionState motion,float progress)
    { motion.duration = motion.pathLength / RuntimeConveyorSpeed; motion.startTime = Time.time; return motion; }
    private bool TryGetRuntimeNextConveyorBlock(out Block next) { next=Next; return next!=null; }
    public bool TryMoveStraightConveyorDataLaneToCached(Block destination,int sourceLane,int destinationLane,float length)
    {
        int id=Id(sourceLane);
        if(id<0 || destination.Id(destinationLane)>=0) return false;
        if(OwnsConveyorTransport)
        {
            if(!ReadTransportLane(sourceLane,out _,out double p)||p<ConveyorTransport.Items.End-0.00001) return false;
        }
        else if(!Ready(sourceLane)) return false;
        if(destination.OwnsConveyorTransport) return destination.TryAcceptTransportTransfer(this,sourceLane,destinationLane);
        var gate=GetConveyorPickupGateStateAtLane(sourceLane);
        ClearConveyorItemAtLane(sourceLane);
        destination.SetConveyorItemAtLane(destinationLane,id,null,gate);
        destination.ready[destinationLane]=Time.time+length/RuntimeConveyorSpeed;
        destination.conveyorItemMotionStates[destinationLane] = new ConveyorDataMotionState {
            active = true, destinationLaneIndex = destinationLane, pathLength = length,
            startWorldPosition = GetConveyorLaneWorldPosition(sourceLane),
            startTime = Time.time, duration = length / RuntimeConveyorSpeed
        };
        return true;
    }
}
