using System;
using System.Collections.Generic;
using UnityEngine;

// Production item store, setters, scheduling, interpolation and stack completion are extracted unchanged.
// Only Unity presentation/camera/clock boundaries are doubled; no Unity process is launched.
public static class TestClock { public static float time; }
public static class InterpolationProbe
{
    public static int Calls;
    public static Vector3 LerpUnclamped(Vector3 start, Vector3 target, float t)
    { Calls++; return Vector3.LerpUnclamped(start, target, t); }
}
public class MoveCullingBoundary
{
    public bool Enabled = true, ForceOutside, UseBounds, LayerVisible = true;
    public Bounds ViewBounds = new(Vector3.zero, new Vector3(40, 10, 40));
    public bool IsLayerVisible(int layer) => LayerVisible;
    public bool Intersects(Bounds bounds) => !ForceOutside && (!UseBounds || ViewBounds.Intersects(bounds));
}
public class EngineTransform
{
    public bool CanvasParent;
    public T GetComponentInParent<T>(bool includeInactive) where T : class => CanvasParent ? new Canvas() as T : null;
    public Vector3 position, localScale;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 TransformPoint(Vector3 local) => position + local;
    public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
    public void SetParent(EngineTransform parent, bool keepWorld) { }
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 lossyScale => Vector3.one;
}
public sealed class Canvas { }
public class EngineObject { public bool activeSelf; public void SetActive(bool value) => activeSelf = value; }
public class EngineView { public readonly EngineTransform transform = new(); public readonly EngineObject gameObject = new(); }
public class Gate
{
    public bool Settled;
    public bool Blocked;
    public void SetAutoPickupBlocked(bool blocked) => Blocked = blocked;
    public void MarkSettled() => Settled = true;
    public void OnOwnerDisabled() { }
}
public class RendererBoundary { public void MarkDirty(PortableObject item) { } }
public partial class PortableObject
{
    readonly PortableObjectWorld world = PortableObjectWorld.Ensure();
    readonly PortableObjectHandle handle;
    EngineView view;
    EngineTransform presentationParent;
    readonly Gate pickupGate = new();
    Gate temporaryDropping;
    RendererBoundary portableItemRenderer;
    bool restoreBatchedRenderingAfterOutline;
    bool HasActiveOutlineRequest => false;
    internal PortableMoveScheduler.MoveState moveState;
    public PortableObject() { handle = world.Create(default, Quaternion.identity, Vector3.one, 0); }
    public bool IsAlive => world.IsAlive(handle);
    public Vector3 WorldPosition => Read().WorldPosition;
    public int Layer => Read().Layer;
    public bool Moving => Read().Moving;
    public Gate PickupGate => pickupGate;
    public Gate GetOrAddPickupGate() => pickupGate;
    public T GetComponent<T>() where T : class => pickupGate as T;
    public PortableObjectComponent Snapshot => Read();
    public int ItemId => Read().ItemId;
    public void SetTestItem(int itemId) { var c = Read(); c.ItemId = itemId; Write(c); }
    void MarkPortableItemRenderDataDirty() { }
    void SyncStateFromView(EngineView v) { }
    void RefreshSleepAwakeVisual(bool force) { }
    void ClearFocusOutlines(bool restore) { }
    void DetachFromFocusOutlineForExternalRenderingChange() { }
    void RefreshPortableItemRendererRegistration() { }
    void UpdateRendererVisibility() { }
    void ReleaseGeneratedPresentationIfPossible() { }
    void ClearBeltItemLineDebugColor() { }
    void SetBodyRendererTemporarilyHidden(bool hidden) { }
    static Vector3 ResolveLocalScale(Vector3 scale, EngineTransform parent) => scale;
    public void Cancel() => CancelScheduledMove();
    public void Dispose() { Cancel(); world.Release(handle); }
}
public partial class PortableMoveScheduler
{
    public static PortableMoveScheduler Current = new();
    internal static PortableMoveScheduler Resolve() => Current;
    readonly List<MoveState> activeMoves = new(100000);
    readonly Stack<MoveState> pooledMoves = new(100000);
    bool enabled;
    readonly MoveCullingBoundary cameraCulling = new();
    static readonly Vector3 MoveCullBoundsSize = new(2, 3, 2);
    bool hasVisibleCellRange;
    float visibleMinimumX, visibleMaximumX, visibleMinimumZ, visibleMaximumZ;
    int lastVisibilityChecks, lastCulledUpdates;
    long totalCulledUpdates;
    public MoveCullingBoundary Culling => cameraCulling;
    public bool Cull { get => cameraCulling.ForceOutside; set => cameraCulling.ForceOutside = value; }
    public int Count => activeMoves.Count;
    public bool PoolCleared => pooledMoves.Count > 0 && pooledMoves.Peek().StackBlock == null && pooledMoves.Peek().Owner == null;
    public void SetVisibleRange(float minX, float maxX, float minZ, float maxZ)
    { hasVisibleCellRange = true; visibleMinimumX = minX; visibleMaximumX = maxX; visibleMinimumZ = minZ; visibleMaximumZ = maxZ; }
    public void ClearVisibleRange() => hasVisibleCellRange = false;
    public void Tick(float now) {
        for (int i = activeMoves.Count - 1; i >= 0; i--) {
            var state = activeMoves[i];
            if (state.Active && !state.Owner.UpdateScheduledMove(this, state, now)) continue;
            RemoveAndPool(i, state);
        }
    }
}
public partial class Block
{
    const float InputAreaCenterVerticalSpacing = .05f;
    public EngineTransform inputAreaCenterAnchor = new(), floorAnchor = new();
    public readonly List<PortableObject> inputAreaCenterStack = new();
    public readonly List<List<PortableObject>> floorStacks = new() { new() };
    public bool Visible = true;
    public Vector2Int Coordinate;
    public Vector3 WorldPosition => new Vector3(Coordinate.x, 0, Coordinate.y);
    float ResolveInputAreaCenterHeight() => .2f;
    EngineTransform ResolveFloorObjectDropAnchor() => floorAnchor;
    Vector3 GetFloorObjectWorldPosition(EngineTransform anchor, int index) => WorldPosition + anchor.position + Vector3.up * (.05f + index * .05f);
    void ConfigureFloorObjectTransform(PortableObject item, EngineTransform anchor, int index) => item.SetWorldPosition(GetFloorObjectWorldPosition(anchor, index));
    void ApplyInputAreaCenterObjectVisibility(PortableObject item, int index) { item.SetWorldPosition(GetItemStackPlacementPosition(true, index)); item.SetCachedActive(Visible); }
}
static class Checks
{
    static int checks;
    static void Require(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Main()
    {
        var scheduler = PortableMoveScheduler.Current;
        var block = new Block(); var item = new PortableObject();
        block.inputAreaCenterStack.Add(item); block.inputAreaCenterAnchor.position = new Vector3(4, 1, 0);
        TestClock.time = 0; int callbacks = 0;
        item.MoveToBlockStack(block, true, 0, .1f, null, () => callbacks++, true, .3f);
        scheduler.Tick(.05f); Require(item.Moving && item.WorldPosition == Vector3.zero && callbacks == 0, "Delay preserves output travel gate");
        scheduler.Tick(.25f); Require(item.Moving && item.WorldPosition.y > 1f, "Jump animation advances");
        block.inputAreaCenterAnchor.position = new Vector3(6, 1, 0);
        scheduler.Cull = true; scheduler.Tick(.5f);
        Require(!item.Moving && item.WorldPosition.x == 6 && item.PickupGate.Settled && callbacks == 1, "Culled landing follows current stack target and completes once");
        Require(scheduler.Count == 0 && scheduler.PoolCleared, "Pool releases block and item references");
        TestClock.time = 1; item.MoveToBlockStack(block, true, 0, 0, null, () => callbacks++, true, .3f); item.Cancel(); scheduler.Tick(2);
        Require(callbacks == 1 && !item.Moving, "Cancelled placement cannot settle or invoke completion");
        block.inputAreaCenterStack[0] = new PortableObject(); item.PickupGate.Settled = false;
        item.MoveToBlockStack(block, true, 0, 0, null, null, false, .3f); scheduler.Tick(2);
        Require(!item.PickupGate.Settled, "Replaced stack item cannot settle its former owner");
        var floor = new PortableObject(); block.floorStacks[0].Add(floor);
        floor.MoveToBlockStack(block, false, 0, 0, null, null, true, .3f); scheduler.Tick(2);
        Require(floor.PickupGate.Settled && floor.WorldPosition.y == .05f && floor.Snapshot.BatchedRendering, "Benchmark floor spill completes batched and pickable");
        PortableMoveScheduler.Current = null; block.Visible = false; block.inputAreaCenterStack[0] = item;
        item.MoveToBlockStack(block, true, 0, 0, null, () => callbacks++, false, .3f);
        Require(callbacks == 2 && !item.Snapshot.Active && item.PickupGate.Settled, "Scheduler unavailable completes hidden center stack immediately");
        PortableMoveScheduler.Current = scheduler; block.Visible = true; scheduler.Cull = false;
        // Real scheduler culling runs before the instrumented interpolation boundary.
        TestClock.time = 3; item.SetWorldPosition(Vector3.zero);
        int mathBefore = InterpolationProbe.Calls, callbackBefore = callbacks;
        item.MoveToBlockStack(block, true, 0, .1f, null, () => callbacks++, true, .3f);
        scheduler.Cull = true; scheduler.Tick(3.25f);
        Require(item.Moving && item.WorldPosition == Vector3.zero && InterpolationProbe.Calls == mathBefore
            && callbacks == callbackBefore, "Culled output keeps only arrival time; no interpolation or early landing");
        scheduler.Cull = false; scheduler.Tick(3.25f);
        Require(item.Moving && Math.Abs(item.WorldPosition.x - 3) < .0001f && item.WorldPosition.y > 1
            && InterpolationProbe.Calls == mathBefore + 1, "View re-entry resumes the current jump phase without restarting");
        scheduler.Cull = true; scheduler.Tick(3.41f); scheduler.Tick(4);
        Require(!item.Moving && item.WorldPosition.x == 6 && callbacks == callbackBefore + 1
            && InterpolationProbe.Calls == mathBefore + 1, "Culled output lands once on time without final interpolation");
        TestClock.time = 4; item.SetWorldPosition(Vector3.zero);
        mathBefore = InterpolationProbe.Calls; callbackBefore = callbacks;
        var inputState = scheduler.Schedule(item, null, new Vector3(8, 0, 0), null, null, Vector3.zero,
            .1f, .3f, true, true, false, () => callbacks++);
        item.moveState = inputState;
        scheduler.Tick(4.25f);
        Require(item.WorldPosition == Vector3.zero && InterpolationProbe.Calls == mathBefore && callbacks == callbackBefore,
            "Culled item input also skips interpolation during transfer");
        scheduler.Tick(4.41f);
        Require(item.WorldPosition == new Vector3(8, 0, 0) && !item.Snapshot.Active && callbacks == callbackBefore + 1
            && InterpolationProbe.Calls == mathBefore, "Culled input reaches destination then deactivates at the arrival deadline");
        var boundsState = new PortableMoveScheduler.MoveState { CullIntermediateUpdates = true, UseJumpArc = true };
        scheduler.Cull = false; scheduler.Culling.UseBounds = true; scheduler.SetVisibleRange(-20, 20, -20, 20);
        Require(scheduler.ShouldSkipIntermediateUpdate(boundsState, new Vector3(100, 0, 100), new Vector3(101, 0, 100)),
            "Entirely offscreen transfer bounds skip interpolation");
        Require(!scheduler.ShouldSkipIntermediateUpdate(boundsState, new Vector3(-100, 0, 0), new Vector3(100, 0, 0)),
            "Flight crossing the view stays visible even if both endpoints are outside");
        Require(!scheduler.ShouldSkipIntermediateUpdate(boundsState, new Vector3(20.5f, 0, 0), new Vector3(21, 0, 0)),
            "Path padding keeps partially visible items at the frustum edge");
        scheduler.Culling.LayerVisible = false;
        Require(scheduler.ShouldSkipIntermediateUpdate(boundsState, Vector3.zero, Vector3.one), "Hidden layers skip interpolation");
        boundsState.CullIntermediateUpdates = false;
        Require(!scheduler.ShouldSkipIntermediateUpdate(boundsState, Vector3.zero, Vector3.one), "UI-bound movement preserves its culling exemption");
        scheduler.Culling.LayerVisible = true; scheduler.Culling.UseBounds = false; scheduler.ClearVisibleRange();
        Require(item.CanCullMoveIntermediateUpdates(null) && item.CanCullMoveIntermediateUpdates(new EngineTransform()),
            "All world transfer targets permit offscreen timing");
        Require(!item.CanCullMoveIntermediateUpdates(new EngineTransform { CanvasParent = true }),
            "Canvas transfer remains independent of world camera culling");
        boundsState.CullIntermediateUpdates = true; scheduler.Culling.Enabled = false;
        Require(!scheduler.ShouldSkipIntermediateUpdate(boundsState, Vector3.zero, Vector3.one), "Disabled camera culling preserves animation");
        scheduler.Culling.Enabled = true;
        TestClock.time = 5; item.SetWorldPosition(Vector3.zero); scheduler.Cull = true; mathBefore = InterpolationProbe.Calls;
        callbackBefore = callbacks;
        item.MoveToBlockStack(block, true, 0, 0, null, () => callbacks++, true, .3f);
        scheduler.Tick(5.15f); item.Cancel(); scheduler.Tick(6);
        Require(callbacks == callbackBefore && InterpolationProbe.Calls == mathBefore && item.WorldPosition == Vector3.zero,
            "Cancelling an offscreen transfer never lands or invokes its callback");
        scheduler.Cull = false;
        // Warm the actual methods/JIT and move-state pool before measuring recurring output costs.
        for (int i = 0; i < 1000; i++) Cycle(item, block, scheduler);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) Cycle(item, block, scheduler);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Require(bytes == 0, $"100k repeated output setters + pooled moves allocated {bytes} bytes");
        Require(item.Snapshot.WorldScale == Vector3.one && !item.Snapshot.OnConveyor && !item.Snapshot.SuppressRendering, "State writes preserve unrelated fields");
        // Reproduce a synchronized burst, not only one repeatedly reused reservation.
        var burstBlock = new Block(); var burst = new PortableObject[100000];
        for (int i = 0; i < burst.Length; i++) {
            burst[i] = new PortableObject(); burstBlock.inputAreaCenterStack.Add(burst[i]);
            burst[i].MoveToBlockStack(burstBlock, true, i, 0, null, null, true, .3f);
        }
        Require(scheduler.Count == burst.Length, "100k simultaneous outputs retain all move reservations");
        scheduler.Tick(TestClock.time + .5f);
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < burst.Length; i++)
            burst[i].MoveToBlockStack(burstBlock, true, i, 0, null, null, true, .3f);
        scheduler.Tick(TestClock.time + .5f);
        long burstBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        int settled = 0;
        for (int i = 0; i < burst.Length; i++) if (burst[i].PickupGate.Settled && !burst[i].Moving) settled++;
        Require(settled == burst.Length && scheduler.Count == 0 && burstBlock.inputAreaCenterStack.Count == burst.Length,
            "100k simultaneous outputs settle without item loss");
        Require(burstBytes == 0, $"Warmed 100k synchronized output scheduling/landing allocated {burstBytes} bytes");
        item.Dispose(); item.SetWorldPose(Vector3.one, Quaternion.identity);
        Require(!item.IsAlive, "Released handle cannot be resurrected by setters");
        DeferredChecks.Run();
        Console.WriteLine($"Portable output: {checks} checks passed; 100,000 repeated cycles: {bytes} bytes; warmed simultaneous 100,000 outputs: {burstBytes} bytes (engine boundaries doubled).");
    }
    static void Cycle(PortableObject item, Block block, PortableMoveScheduler scheduler)
    {
        item.SetWorldPose(Vector3.zero, Quaternion.identity); item.SetWorldScale(Vector3.one);
        item.SetConveyorOwnership(true); item.SetConveyorOwnership(false);
        item.SetSleepAwakeSleeping(true); item.SetSleepAwakeSleeping(false);
        item.SetCachedActive(false); item.SetCachedActive(true);
        item.SetVisualRenderingSuppressed(true); item.SetVisualRenderingSuppressed(false);
        item.SetBatchedRendering(false); item.SetBatchedRendering(true);
        item.SetLocalPose(null, Vector3.zero, Quaternion.identity, Vector3.one);
        item.MoveToBlockStack(block, true, 0, 0, null, null, true, .3f); scheduler.Tick(TestClock.time + .5f);
    }
}
