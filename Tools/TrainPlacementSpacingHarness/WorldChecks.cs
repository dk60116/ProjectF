using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using ProjectF.Railway;
using UnityEngine;

static class WorldChecks
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    public static void Run()
    {
        var world = new TrainWorld();
        var first = world.Create();
        var second = world.Create();
        var third = world.Create();
        Check(world.Count == 3 && !first.IsActive && !first.IsPlaced, "Data allocation must not imply placement/activation");
        var foreign = new TrainWorld().Create();
        Check(!world.Connect(first, foreign, true, false) && !world.Connect(first, first, true, false),
            "Connections must reject foreign worlds and self-coupling");

        Check(world.Connect(first, second, true, true), "Data-only cars must connect without views");
        ulong revision = world.ConnectionGraphRevision;
        Check(first.ConnectionCount == 1 && second.ConnectionCount == 1, "Coupling must be reciprocal");
        Check(first.TryGetConnectionFacingSign(second, true, out float sign) && sign == 1,
            "Physical front coupling must align with an ahead car");
        Check(second.TryGetConnectionFacingSign(first, false, out sign) && sign == -1,
            "A reverse-facing car must preserve its own front coupling");
        Check(!world.Connect(second, first, false, false) && world.ConnectionGraphRevision == revision,
            "Repeated coupling must not rewrite physical ends or invalidate route caches");
        world.SetConnectionEnd(first, second, true);
        Check(world.ConnectionGraphRevision == revision, "An unchanged coupling end must not invalidate the graph");
        world.SetConnectionEnd(first, second, false);
        Check(world.ConnectionGraphRevision == revision + 1 && first.TryGetConnectionFacingSign(second, true, out sign) && sign == -1,
            "Changing a physical coupling end must invalidate cached graph plans");
        world.Connect(second, third, false, true);
        world.Release(second);
        Check(!second.IsAlive && first.ConnectionCount == 0 && third.ConnectionCount == 0,
            "Releasing a car must unlink every neighboring data car");
        var replacement = world.Create();
        Check(replacement.IsAlive && !ReferenceEquals(second, replacement), "Pool reuse must not revive old entity references");
        world.SetConnectionEnd(first, second, true);
        Check(!world.Connect(first, second, true, false), "Released entities must never rejoin a graph");

        var neighbors = new List<TrainInstance>(8);
        world.SetWorldPose(first, new Vector3(.49f, 2, -.49f), Quaternion.identity);
        world.SetPlacement(first, true, 100);
        world.SetActive(first, true);
        world.CollectNearby(Vector2.zero, 0, neighbors);
        Check(neighbors.Count == 1 && neighbors[0] == first, "Placed active cars must enter the world coordinate index");
        world.SetWorldPose(first, new Vector3(.51f, 2, -.51f), Quaternion.identity);
        world.CollectNearby(Vector2.zero, 0, neighbors);
        Check(neighbors.Count == 0, "Crossing a cell must remove the old coordinate membership");
        world.CollectNearby(new Vector2(1, -1), 0, neighbors);
        Check(neighbors.Count == 1 && first.Coordinate == new Vector2Int(1, -1), "Cell crossing must retain one current membership");
        world.SetPlacement(first, true, 101);
        world.SetPlacement(first, true, 101);
        world.CollectNearby(new Vector2(1, -1), 0, neighbors);
        Check(neighbors.Count == 1, "Repeated placement refresh must not duplicate indexed cars");
        world.SetActive(first, false);
        world.CollectNearby(new Vector2(1, -1), 0, neighbors);
        Check(neighbors.Count == 0, "Disabled cars must leave the active index");
        world.SetActive(first, true);
        world.SetPlacement(first, false, 0);
        world.CollectNearby(new Vector2(1, -1), 0, neighbors);
        Check(neighbors.Count == 0, "Preview/unplaced cars must not obstruct runtime contact searches");

        foreach (Vector2 axis in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
        foreach (bool reversed in new[] { false, true })
        foreach (float physicalSign in new[] { -1f, 1f })
        {
            var source = reversed ? new Railload(axis * 4, Vector2.zero) : new Railload(Vector2.zero, axis * 4);
            var target = new Railload(axis * 4 + new Vector2(.1f, .2f), axis * 8);
            Vector2 front = axis * physicalSign;
            first.SetRailSample(source, 2, axis * 2, front);
            Check(first.TryGetRailPose(out var found, out float distance, out var point, out var forward)
                && found == source && Math.Abs(distance - 2) < .0001f && Vector2.Dot(forward, front) > .999f,
                "Data rail sampling must align raw authoring order to the physical front");
            Vector2 bridgePoint = axis * 4 + new Vector2(.05f, .1f);
            first.SetRailSample(source, reversed ? 0 : 4, bridgePoint, front);
            first.ConfigureRailConnectionTransition(target, 0, axis * 4 + new Vector2(.1f, .2f), front, .3f, .1f);
            Check(first.TryGetRailPose(out _, out _, out point, out forward) && point == bridgePoint && forward == front,
                "The stored junction point/front must survive without a rendering Transform");
            Check(first.TryGetRailConnectionTransition(out found, out distance, out point, out forward, out float length, out float progress)
                && found == target && Math.Abs(length - .3f) < .0001f && Math.Abs(progress - .1f) < .0001f,
                "Bridge destination and progress must remain data-owned");
            first.ConfigureRailConnectionTransition(target, 0, point, front, .3f, 2);
            Check(first.ConnectionProgressUnits == first.ConnectionLengthUnits, "Bridge progress must clamp to its length");
            target.Alive = false;
            Check(!first.TryGetRailConnectionTransition(out _, out _, out _, out _, out _, out _),
                "A removed destination rail must not expose a valid bridge");
            first.SetRailSample(source, 2, axis * 2, front);
            Check(first.ConnectionTargetRail == null && first.ConnectionProgressUnits == 0, "Applying a new rail pose must clear the previous bridge");
            source.Alive = false;
            Check(!first.TryGetRailPose(out _, out _, out _, out _), "Removed rails must invalidate stored poses");
        }

        var engine = new TrainMotionProbe();
        var handcart = new VehicleMotionProbe();
        engine.RuntimeTrain.Motion.SignedSpeed = -1;
        Check(engine.Speed == -1 && handcart.Speed == 0, "Train reads must use world speed without changing ordinary vehicles");
        Check(Math.Abs(engine.Drive(1, .1f) + .6f) < .0001f && engine.Speed == engine.RuntimeTrain.Motion.SignedSpeed,
            "The production speed integrator must write directly to train data");
        engine.RuntimeTrain.Motion.SignedSpeed = 5;
        engine.Clamp(1);
        Check(engine.RuntimeTrain.Motion.SignedSpeed == 1, "Speed clamps must target the data state");
        handcart.Drive(-1, .2f);
        engine.Stop();
        Check(engine.RuntimeTrain.Motion.SignedSpeed == 0 && handcart.Speed < 0,
            "Stopping a train must not mutate a non-rail vehicle's native motion state");

        // A rendering binding can disappear while calculation state and graph survive.
        var a = new Train();
        var b = new Train();
        var nativeWorld = TrainWorld.Shared;
        nativeWorld.Connect(a.RuntimeTrain, b.RuntimeTrain, true, false);
        Check(a.ConnectedTrains.Count == 1, "Legacy native enumeration must resolve the data graph");
        b.RuntimeTrain.Motion.SignedSpeed = -.75f;
        nativeWorld.DetachView(b.RuntimeTrain);
        Check(a.RuntimeTrain.ConnectionCount == 1 && b.RuntimeTrain.Motion.SignedSpeed == -.75f && a.ConnectedTrains.Count == 0,
            "Removing a render binding must not own or discard connection/speed state");
        nativeWorld.AttachView(b.RuntimeTrain, b);
        Check(a.ConnectedTrains.Count == 1, "Reattaching the same view must preserve existing links");
        int enumerated = 0;
        foreach (var car in a.ConnectedTrains) { Check(car == b, "Native enumerator must map the data neighbor"); enumerated++; }
        Check(enumerated == 1, "Native iteration must return each neighbor once");
        nativeWorld.Release(b.RuntimeTrain);
        Check(a.ConnectedTrains.Count == 0 && a.RuntimeTrain.ConnectionCount == 0,
            "Pooled native cars must disappear from reciprocal data links");

        // Warm buckets and connection capacity, then measure the actual production paths.
        world.SetPlacement(first, true, 100);
        world.SetActive(first, true);
        world.Connect(first, replacement, true, false);
        world.ClearConnections(first);
        for (int i = 0; i < 1000; i++) Exercise(world, first, replacement, neighbors, i);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) Exercise(world, first, replacement, neighbors, i);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated == 0, "Warm movement/query/coupling/clear paths must allocate zero bytes, got " + allocated);

        for (int i = 0; i < 10000; i++)
        {
            var distant = world.Create();
            world.SetWorldPose(distant, new Vector3(100 + i * 3, 0, 100), Quaternion.identity);
            world.SetPlacement(distant, true, i + 1000);
            world.SetActive(distant, true);
        }
        world.CollectNearby(Vector2.zero, 5, neighbors);
        Check(neighbors.Count == 1 && neighbors[0] == first,
            "Ten thousand distant cars must not enter a local contact candidate list");

        var chain = new TrainInstance[1000];
        for (int i = 0; i < chain.Length; i++)
        {
            chain[i] = world.Create();
            if (i > 0) world.Connect(chain[i - 1], chain[i], true, false);
        }
        world.Release(chain[500]);
        Check(chain[499].ConnectionCount == 1 && chain[501].ConnectionCount == 1,
            "Removing the middle of a long consist must only unlink its own two edges");
        world.Clear();
        Check(world.Count == 0 && !first.IsAlive && chain[499].ConnectionCount == 0,
            "World reset must invalidate all old identities and links");
        Check(!first.TryGetRailPose(out _, out _, out _, out _), "Released data must not return a rail pose after world reset");
        world.CollectNearby(Vector2.zero, 100, neighbors);
        Check(neighbors.Count == 0, "World reset must clear the spatial index");
        RunNativeLifecycle();
        Console.WriteLine($"Train data world passed: {checks} checks; warm paths {allocated} bytes allocated");
    }

    static void RunNativeLifecycle()
    {
        var world = TrainWorld.Shared;
        world.Clear();
        var rail = new Railload(Vector2.zero, Vector2.up * 10);
        var car = new Train { RuntimePlacementSequence = 10 };
        var neighbor = new Train { RuntimePlacementSequence = 11 };
        car.TryApplyRailPose(rail, 1, Vector2.up, Vector2.up);
        neighbor.TryApplyRailPose(rail, 2, Vector2.up * 2, Vector2.up);
        var state = car.RuntimeTrain;
        var firstHandle = new MapObjectHandle(10, 1, 1, 10);
        car.BindHandle(firstHandle);
        Check(state.Handle == firstHandle && state.IsActive && state.IsPlaced && state.PlacementSequence == 10,
            "Native placement and map identity must bind to the same data entity");
        long exactUnits = DeterministicSimulationUnits.UnitsPerWhole + 1;
        car.ApplyPlacedRailSampleUnits(rail, exactUnits, Vector2.up, Vector2.up);
        Check(car.TryGetCurrentRailPoseUnits(out _, out long restoredUnits, out _, out _) && restoredUnits == exactUnits,
            "The units adapter must preserve exact saved rail distances beyond float precision");
        car.BindHandle(default);
        car.BindHandle(new MapObjectHandle(10, 1, 2, 10));
        Check(ReferenceEquals(car.RuntimeTrain, state) && state.RailDistanceUnits == exactUnits
            && state.Handle.Generation == 2, "Temporary map handle rebinding must not replace a moving car's state");
        Check(car.TryGetTouchingUnconnectedTrain(out Train contact) && contact == neighbor,
            "The production native contact search must find indexed cars");
        Check(car.ConnectTo(neighbor), "Native contact commit must update the data graph");
        car.RuntimeTrain.Motion.SignedSpeed = 1;
        car.Disable();
        var nearby = new List<TrainInstance>();
        world.CollectNearby(Vector2.up, 0, nearby);
        Check(nearby.Count == 0 && !state.IsActive && state.Motion.SignedSpeed == 0,
            "Actual Train/Vehicle disable callbacks must remove membership and reset data speed");
        Check(neighbor.ConnectedTrains.Count == 0 && state.ConnectionCount == 0,
            "Disable must remove reciprocal physical connections");
        Check(car.TryGetCurrentRailPose(out _, out _, out _, out _),
            "Temporary native disable must retain the placed rail sample");
        car.Enable();
        Check(ReferenceEquals(state, car.RuntimeTrain) && state.IsActive && state.IsPlaced,
            "Temporary disable/enable must preserve the data identity");
        car.ConnectTo(neighbor);
        car.RuntimeTrain.ConfigureRailConnectionTransition(rail, 2, Vector2.up * 2, Vector2.up, .2f, .1f);
        int beforePool = world.Count;
        car.PrepareForPool();
        Check(world.Count == beforePool - 1 && !state.IsAlive && state.Handle == default
            && state.Rail == null && state.ConnectionTargetRail == null && state.Motion.SignedSpeed == 0,
            "The production pool callback must release all calculation state");
        car.Disable(); // InstallationObjectPool invokes this AFTER PrepareForPool.
        car.ClearTrainConnections();
        car.ClearPlacedRailSample();
        car.BindHandle(default);
        Check(!car.TryGetCurrentRailPose(out _, out _, out _, out _) && car.ConnectedTrains.Count == 0
            && world.Count == beforePool - 1, "Cleanup and read-only queries after pool release must not create a ghost entity");
        car.Enable();
        var reused = car.RuntimeTrain;
        Check(!ReferenceEquals(state, reused) && reused.IsAlive && !reused.IsPlaced
            && reused.Rail == null && reused.Handle == default && reused.Motion.SignedSpeed == 0,
            "Borrowing a pooled view must create a fresh identity without old rail/coupling/speed state");
        state.Motion.SignedSpeed = 8;
        Check(car.Speed == 0, "References to released state must not write to a reused view");
        int beforeDestroy = world.Count;
        car.DestroyView();
        Check(world.Count == beforeDestroy - 1 && !reused.IsAlive, "Actual native destruction must release state even when unplaced");

        var leader = new Train { RuntimePlacementSequence = 100 };
        var ahead = new Train { RuntimePlacementSequence = 101 };
        var behind = new Train { RuntimePlacementSequence = 102 };
        leader.TryApplyRailPose(rail, 5, Vector2.up * 5, Vector2.up);
        ahead.TryApplyRailPose(rail, 6, Vector2.up * 6, Vector2.up);
        behind.TryApplyRailPose(rail, 3.7f, Vector2.up * 3.7f, Vector2.up);
        Check(leader.TryGetTouchingUnconnectedTrain(out contact) && contact == ahead,
            "Indexed contact search must still choose the nearest valid neighbor");
        leader.ConnectTo(ahead);
        Check(leader.TryGetTouchingUnconnectedTrain(out contact) && contact == behind,
            "The spatial query must exclude the current connected consist");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            foreach (var linked in leader.ConnectedTrains) { if (linked != ahead) throw new Exception("Wrong native link"); }
            if (leader.ConnectedTrains.Count != 1) throw new Exception("Wrong native count");
            if (!leader.TryGetTouchingUnconnectedTrain(out _)) throw new Exception("Missing native contact");
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Check(bytes == 0, "Warm native enumeration/contact search must allocate zero bytes, got " + bytes);
        world.Clear();
    }

    static void Exercise(TrainWorld world, TrainInstance car, TrainInstance other, List<TrainInstance> nearby, int iteration)
    {
        world.SetWorldPose(car, new Vector3(iteration % 4, 0, 0), Quaternion.identity);
        world.CollectNearby(Vector2.zero, 5, nearby);
        world.Connect(car, other, true, false);
        world.ClearConnections(car);
        car.Motion.Advance(.5f, 1f / 60, 2, 4, 4);
    }
}
