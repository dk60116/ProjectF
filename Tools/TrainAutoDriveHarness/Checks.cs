using System;
using System.Collections.Generic;
using UnityEngine;

// Engine objects, station lookup, fuel storage and motion are substituted.
// Selection, control transfer, schedule state, forward guards and graph search
// are extracted from production by Run.ps1.
static class Time { public static int frameCount = 1; }
public class Player { }
public class FakeObject { public bool activeInHierarchy = true; }
public class FakeTransform { public Vector3 forward; }
public partial class Railload
{
    public Vector2 Origin;
    public Vector2 Direction = Vector2.right;
    public bool TrySampleRenderedPath(float distance, out Vector2 point, out Vector2 tangent)
    {
        if (CurveRadius > 0f) { SampleCurve(distance, out point, out tangent); return true; }
        tangent = Direction; point = Origin + Direction * distance; return true;
    }
}
public class Trainstation
{
    public string StationName;
    public int Distance;
    public bool TryGetRailCoordinate(out Vector2Int coordinate)
    { coordinate = new Vector2Int(Distance, 0); return true; }
}
public partial class Train
{
    public static ulong ConnectionGraphRevision = 1;
    static int nextId;
    readonly int id = ++nextId;
    public readonly FakeObject gameObject = new();
    public readonly FakeTransform transform = new();
    public readonly List<Train> ConnectedTrains = new();
    public virtual bool BlocksManualDisconnection => false;
    public Railload Rail;
    public float Distance;
    public int GetInstanceID() => id;
    public bool TryGetPlacementRuntime(out int a, out int b) { a = b = 0; return Rail != null; }
    public bool TryGetCurrentRailPose(out Railload rail, out float distance, out Vector2 point, out Vector2 tangent)
    { rail = Rail; distance = Distance; Rail.TrySampleRenderedPath(distance, out point, out _); tangent = new(transform.forward.x, transform.forward.z); return true; }
    public static void Link(Train a, Train b)
    { a.ConnectedTrains.Add(b); b.ConnectedTrains.Add(a); ConnectionGraphRevision++; }
}
public partial class RailHandcar : Train
{
    readonly List<Train> connectedTrainGroupScratch = new();
    readonly Dictionary<float, float> testStationDockDeltas = new();
    public float CurrentVehicleSignedSpeed;
    public int PoweredMoves;
    public bool TryGetRailForwardDirection(out Vector2 direction)
    { direction = new(transform.forward.x, transform.forward.z); return true; }
    public bool TryGetRailDockDeltaAtCoordinate(Vector2Int coordinate, out float delta)
    { delta = coordinate.x - Distance; return true; }
    public virtual void HandleMountedInput(Vector3 direction, float speed, float dt, Player player) { }
    public void HandleMountedInput(Vector3 direction, float speed, float dt)
    {
        bool hasInput = direction.sqrMagnitude > .0001f;
        Vector2 input = new(direction.x, direction.z);
        TryGetRailForwardDirection(out Vector2 facing);
        var sample = new RailSample { Rail = Rail, DistanceAlongPath = Distance, Tangent = Rail.Direction };
        float axis = ResolveRailInputAxis(hasInput, input, input.magnitude, facing, sample);
        if (hasInput) { CurrentVehicleSignedSpeed = axis; PoweredMoves++; }
        float step = AdjustDrivenSignedStep(sample, facing, hasInput, input, dt, CurrentVehicleSignedSpeed * dt);
        if (hasInput) LastStep = step;
    }
    public float LastStep;
    protected void ResetVehicleMotion() { CurrentVehicleSignedSpeed = 0; }
    float stationDockSpeed = 1;
    protected float ResolveDockCompleteDistance() => .01f;
    public float DockMovement;
    protected bool Dock(float delta, bool snap)
    {
        TryGetRailForwardDirection(out Vector2 facing);
        var current = new RailSample { Rail = Rail, DistanceAlongPath = Distance, Tangent = Rail.Direction, Point = Rail.Direction * Distance };
        var dock = new RailSample { Rail = Rail, DistanceAlongPath = Distance + delta, Tangent = Rail.Direction, Point = Rail.Direction * (Distance + delta) };
        return TryApplyDockingToSample(current, facing, dock, delta, .1f, false, snap);
    }
    protected bool WaterDock(float delta)
    {
        TryGetRailForwardDirection(out Vector2 facing);
        var current = new RailSample { Rail = Rail, DistanceAlongPath = Distance, Tangent = Rail.Direction, Point = Rail.Direction * Distance };
        var dock = new RailSample { Rail = Rail, DistanceAlongPath = Distance + delta, Tangent = Rail.Direction, Point = Rail.Direction * (Distance + delta) };
        return TryApplyDockingToSample(
            current, facing, dock, delta, .1f,
            preserveCoastTravelDirection: true,
            allowReverseDocking: true);
    }
    void ApplyRailPose(RailSample sample, Vector2 facing, float dt, bool snap)
    { DockMovement += (sample.DistanceAlongPath - Distance) * Vector2.Dot(Rail.Direction, facing); }
    bool TryMoveConnectedTrainGroupForDocking(RailSample current, Vector2 facing, Vector2 travel, float step, float dt, bool preserve)
    { DockMovement += step * Vector2.Dot(facing, travel); return true; }
    protected virtual float ResolveRailInputAxis(bool hasInput, Vector2 input, float magnitude, Vector2 facing, RailSample sample)
        => hasInput ? Vector2.Dot(input.normalized, facing) * magnitude : 0;
    protected virtual float AdjustDrivenSignedStep(RailSample sample, Vector2 facing, bool hasInput, Vector2 input, float dt, float step) => step;
    void CollectConnectedTrainGroupForMovement(Train start)
    {
        connectedTrainGroupScratch.Clear();
        var queue = new Queue<Train>();
        var visited = new HashSet<Train>();
        queue.Enqueue(start); visited.Add(start);
        while (queue.Count > 0)
        {
            Train current = queue.Dequeue();
            connectedTrainGroupScratch.Add(current);
            foreach (Train next in current.ConnectedTrains)
                if (visited.Add(next)) queue.Enqueue(next);
        }
    }
    bool TryFindStationDockSample(RailSample current, out RailSample dock, out float delta)
    {
        dock = current;
        if (!testStationDockDeltas.TryGetValue(current.DistanceAlongPath, out delta)) return false;
        dock.DistanceAlongPath += delta;
        dock.Point += current.Rail.Direction * delta;
        return true;
    }
    public void SetTestStationDock(float memberDistance, float delta)
        => testStationDockDeltas[memberDistance] = delta;
}

public partial class SteamTrain
{
    const float AutoDriveRouteSegmentTolerance = .2f, AutoDriveRouteRefreshInterval = .25f;
    const float AutoDriveWaitDurationSeconds = 5, BurnEnergyEpsilon = .0001f, WaterEpsilon = .0001f;
    static readonly Railload TestRail = new();
    static readonly Trainstation StationA = new() { StationName = "A", Distance = 0 };
    static readonly Trainstation StationB = new() { StationName = "B", Distance = 20 };
    bool HasAnyAutoDriveTarget => true;
    public bool HasFuel = true;
    float pendingBurnEnergyCost, pendingWaterCost;
    int pendingBurnEnergyFrame, pendingWaterFrame, fuelRequests;
    bool testDock;
    Vector2 testDockDirection;
    float testDockDistance;
    public bool HasFluidStorageSpace = true;
    public bool HasWaterDock;
    public float WaterDockDelta;
    public bool WaterPipeReady;
    void ClearPendingBurnEnergyCost() { pendingBurnEnergyCost = 0; }
    void ClearPendingWaterCost() { pendingWaterCost = 0; }
    void RequestWaterPipeRetract() { WaterPipeReady = false; }
    void SetWaterPipeDockTarget(Vector2Int direction, bool ready) { WaterPipeReady = ready; }
    bool TryResolveWaterPipeDockSample(
        RailSample currentSample,
        Vector2 currentFacing,
        out RailSample dockSample,
        out float signedPathDelta,
        out Vector2Int directionFromTrainToPipe,
        out Vector2 dockFacing)
    {
        dockSample = currentSample;
        signedPathDelta = WaterDockDelta;
        dockSample.DistanceAlongPath += signedPathDelta;
        dockSample.Point += currentSample.Rail.Direction * signedPathDelta;
        directionFromTrainToPipe = Vector2Int.up;
        dockFacing = currentFacing;
        return HasWaterDock;
    }
    public bool TestConsistWaterDocking()
    {
        TryBuildCurrentRailSample(this, out RailSample sample);
        return TryApplyConsistWaterPipeDocking(
            sample,
            new Vector2(transform.forward.x, transform.forward.z),
            .1f);
    }
    public bool TestConsistStationDocking()
    {
        TryBuildCurrentRailSample(this, out RailSample sample);
        return TryApplyStationDocking(
            sample,
            new Vector2(transform.forward.x, transform.forward.z),
            .1f);
    }
    void StopMovementParticle(bool unused) { }
    void PersistAutoDriveState() { }
    bool RequiresWater(Vector3 direction, float dt, out float cost) { cost = 0; return false; }
    bool TryEnsureWaterAvailable(float cost) => true;
    bool RequiresPoweredBurnEnergy(Vector3 direction, float dt, out float cost)
    { cost = direction.sqrMagnitude > .0001f ? dt : 0; return cost > 0; }
    bool TryEnsureBurnEnergyAvailable(float cost, Player player) { fuelRequests++; return HasFuel; }
    void SetAutoDriveStatus(AutoDriveStatus status, string current, string next)
    { autoDriveStatus = status; autoDriveCurrentTargetStationName = current; autoDriveNextTargetStationName = next; }
    string ResolveAutoDriveStatusText() => autoDriveStatus.ToString();
    float ResolveDockCaptureDistance() => .5f;
    float ResolveAutoDriveArrivalSnapDistance() => .05f;
    float ResolveAutoDriveDockApproachDistance() => 1;
    bool TryGetAutoDriveTargetDockDistance(Trainstation target, out float distance)
    { distance = Math.Abs(target.Distance - Distance); return true; }
    bool TrySnapAutoDriveToTargetDock(Trainstation target, float dt) => Math.Abs(target.Distance - Distance) < .0001f;
    bool TryFinalizeAutoDriveArrival(float dt) => false;
    bool TryGetAutoDriveTargetDockPathDelta(Trainstation target, out float delta, out Vector2 direction)
    { delta = testDockDistance; direction = testDockDirection; return testDock; }
    bool TryApplyAutoDriveDockApproachSpeed(ref Vector3 direction, float distance) => false;
    bool IsAutoDriveDockingApproachActive() => testDock;
    bool HasAutoDriveRouteReferenceChanged(RailHandcar train) => train.GetInstanceID() != autoDriveRouteReferenceTrainInstanceId;
    void ReconcileAutoDriveRouteCursor(Railload rail, float distance) { }
    bool TryFindBestAutoDriveRouteSegmentIndex(Railload rail, float distance, out int index)
    { index = 0; return autoDriveRouteSegments.Count > 0; }
    bool TryResolveAutoDriveRouteMoveDirection(out Vector3 direction)
    {
        var segment = autoDriveRouteSegments[0];
        var tangent = segment.Rail.Direction * Math.Sign(segment.EndDistance - segment.StartDistance);
        direction = new Vector3(tangent.x, 0, tangent.y);
        return true;
    }
    bool TryEnsureAutoDriveFixedRoute()
    {
        if (autoDriveFixedRouteSegments.Count == 0)
        {
            autoDriveFixedRouteSegments.Add(new AutoDriveRoutePlanner.RouteSegment(TestRail, 0, 20));
            autoDriveFixedRouteStartStationName = "A";
            autoDriveFixedRouteEndStationName = "B";
        }
        return true;
    }
    private static partial class AutoDriveRoutePlanner
    {
        public static int RouteGraphVersion => 1;
        public static bool TryFindStationByName(string name, out Trainstation station)
        { station = name == "A" ? StationA : name == "B" ? StationB : null; return station != null; }
        public static bool TryBuildRoute(Train train, Trainstation target, List<RouteSegment> result)
        {
            result.Clear(); result.Add(new RouteSegment(train.Rail, train.Distance, target.Distance));
            return IsForwardRoute(train, result);
        }

        public static void CheckGraph()
        {
            var rails = new[] { new RailInfo { Rail = TestRail } };
            var result = new List<RouteSegment>();
            var reverseOnly = new[] {
                new List<RouteGraphEdge> { new(1, 0, 10, 0, 10) }, new List<RouteGraphEdge>() };
            Check(!TryFindRouteGraphPath(rails, reverseOnly, 0, 1, Vector2.right, result), "Graph must reject a reverse-only departure");
            Check(TryFindRouteGraphPath(rails, reverseOnly, 0, 1, Vector2.left, result), "Opposite locomotive must reach the same destination forward");
            var reverseTurn = new[] {
                new List<RouteGraphEdge> { new(1, 0, 0, 10, 10) },
                new List<RouteGraphEdge> { new(2, 0, 10, 5, 5) }, new List<RouteGraphEdge>() };
            result.Clear();
            Check(!TryFindRouteGraphPath(rails, reverseTurn, 0, 2, Vector2.right, result), "Graph must reject reversal at a rail connection");
            var alternative = new[] {
                new List<RouteGraphEdge> { new(2, 0, 10, 0, 1), new(1, 0, 10, 15, 1500) },
                new List<RouteGraphEdge> { new(2, 0, 15, 20, 1500) }, new List<RouteGraphEdge>() };
            result.Clear();
            Check(TryFindRouteGraphPath(rails, alternative, 0, 2, Vector2.right, result) && result[0].EndDistance > result[0].StartDistance,
                "A long forward route must beat a cheap reverse route even beyond the old 1000 penalty");
        }
    }

    static int checks;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
    static SteamTrain Engine(float distance, Vector2 facing)
    { var t = new SteamTrain { Rail = TestRail, Distance = distance }; t.transform.forward = new(facing.x, 0, facing.y); return t; }
    static void Main()
    {
        AutoDriveRoutePlanner.CheckGraph();
        var left = Engine(4, Vector2.left);
        var right = Engine(7, Vector2.right);
        var wagon = new Train { Rail = TestRail, Distance = 5 };
        Train.Link(left, wagon); Train.Link(wagon, right);
        left.ApplyAutoDriveState(true, "A", "B", 1, 2, "B", "A", 0);
        left.TickAutoDrive(.1f, null);
        Check(!left.autoDriveEnabled && right.autoDriveEnabled, "Control must move to the destination-side locomotive");
        Check(left.PoweredMoves == 0 && right.PoweredMoves == 1, "Only the selected locomotive may accelerate");
        Check(left.fuelRequests == 0 && right.fuelRequests == 1, "Fuel must be requested from the selected locomotive");
        Check(right.LastStep > 0, "Automatic movement must be forward relative to the selected locomotive");
        Check(right.autoDriveLastArrivedStationName == "A" && right.autoDriveResolvedTargetStationName == "B", "Handoff must preserve the current service leg");
        Check(left.AutoDriveEnabled && left.AutoDriveFuelFilterName == "Full" && left.AutoDriveFreightFilterName == "Empty", "Old locomotive UI must follow the active schedule");
        Check(left.BlocksManualDisconnection && right.BlocksManualDisconnection, "Automatic driving must lock manual disconnection from either locomotive");
        left.HandleMountedInput(Vector3.left, 1, .1f, new Player());
        right.TickAutoDrive(.1f, null);
        Check(right.PoweredMoves == 1 && left.PoweredMoves == 0, "Mounted input and both Update calls must not drive twice in one frame");
        Check(right.pendingBurnEnergyCost > 0, "Duplicate mounted input must not clear pending fuel cost");

        Time.frameCount++;
        right.SeedStraightPath(left, wagon, right);
        right.autoDriveLastArrivedStationName = "B";
        right.autoDriveStationWaitTimer = 3;
        right.TickAutoDrive(.1f, null);
        Check(left.autoDriveEnabled && !right.autoDriveEnabled, "Return service must select the opposite locomotive");
        Check(left.HasRecordedPath && !right.HasRecordedPath, "Schedule handoff must transfer the actual movement tape to the return locomotive");
        Check(Near(left.autoDriveStationWaitTimer, 2.9f), "Station wait must survive handoff and tick once");
        Check(left.autoDriveResolvedTargetStationName == "A", "Return service must retain the destination");
        left.CaptureAutoDriveState(out bool savedEnabled, out _, out _, out _, out _, out string savedTarget, out string savedArrival, out float savedWait);
        right.CaptureAutoDriveState(out bool oldEnabled, out _, out _, out _, out _, out _, out _, out _);
        Check(savedEnabled && !oldEnabled && savedTarget == "A" && savedArrival == "B" && Near(savedWait, 2.9f), "Only the actual controller schedule must be serialized as enabled");

        Time.frameCount++;
        left.autoDriveStationWaitTimer = 0; left.HasFuel = false;
        left.TickAutoDrive(.1f, null);
        Check(left.autoDriveStatus == AutoDriveStatus.WaitingForFuel && right.fuelRequests == 1, "Empty leading locomotive must wait instead of reversing with the other engine");
        right.ApplyAutoDriveSettings(false, "A", "B", "Full", "Empty");
        Check(!left.AutoDriveEnabled && !right.AutoDriveEnabled, "Stopping from the old locomotive UI must stop the current controller");
        Check(!left.BlocksManualDisconnection && !right.BlocksManualDisconnection, "Stopping automatic driving must unlock manual disconnection");
        Time.frameCount++;
        right.HandleMountedInput(Vector3.left, 1, .1f, new Player());
        Check(right.LastStep < 0, "Manual reverse must remain available");

        // Both powered vehicles face the same way: choose the closer forward one.
        var trailing = Engine(4, Vector2.right); var leading = Engine(7, Vector2.right);
        Train.Link(trailing, leading);
        trailing.CollectAutoDriveConnectedTrains();
        Check(trailing.TryResolveAutoDriveClosestEndpointTrain(StationB, "B", out var chosen) && chosen == leading, "Shortest forward rail distance must select the closer powered vehicle");
        var handcar = new RailHandcar { Rail = TestRail, Distance = 19 };
        handcar.transform.forward = Vector3.right;
        Train.Link(leading, handcar);
        trailing.CollectAutoDriveConnectedTrains();
        Check(!trailing.TryGetAutoDriveRouteReferenceCandidate(handcar, false, out _), "A manual handcar must not become the automatic power source");

        // A lone locomotive pointing away from the target must wait.
        var lone = Engine(7, Vector2.left);
        lone.ApplyAutoDriveState(true, "A", "B", 0, 0, "B", "A", 0);
        lone.CurrentVehicleSignedSpeed = -2;
        Time.frameCount++; lone.TickAutoDrive(.1f, null);
        Check(lone.PoweredMoves == 0 && Near(lone.CurrentVehicleSignedSpeed, 0) && lone.autoDriveStatus == AutoDriveStatus.WaitingForPath,
            "No forward locomotive means waiting, with manual reverse momentum cleared");

        foreach (bool coupled in new[] { false, true })
        foreach (bool snap in new[] { false, true })
        {
            var engine = Engine(7, Vector2.right);
            if (coupled) Train.Link(engine, new Train { Rail = TestRail });
            engine.autoDriveEnabled = true;
            Check(!engine.Dock(-.02f, snap) && Near(engine.DockMovement, 0), "Automatic station docking must reject reverse movement before both snap and consist movement");
            Check(engine.Dock(.02f, snap) && engine.DockMovement > 0, "Forward automatic docking must still work");
            Check(engine.Dock(0, snap), "An aligned locomotive must be able to complete docking without movement");
            engine.DockMovement = 0;
            Check(engine.WaterDock(-.02f) && engine.DockMovement < 0,
                "A water pipe on the opposite side must allow its short docking correction");
            engine.autoDriveEnabled = false; engine.DockMovement = 0;
            Check(engine.Dock(-.02f, snap) && engine.DockMovement < 0, "Manual backward docking must still work");
        }

        foreach (Vector2 facing in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            var engine = Engine(7, facing); engine.autoDriveEnabled = true;
            var sample = new RailSample { Rail = TestRail, DistanceAlongPath = 7, Tangent = facing };
            Check(engine.ResolveRailInputAxis(true, -facing, 1, facing, sample) == 0, "Auto reverse input must be blocked on every heading");
            Check(engine.AdjustDrivenSignedStep(sample, facing, false, Vector2.zero, .1f, -.3f) == 0, "Auto reverse coasting must be blocked");
            Check(!engine.CanDockInDirection(facing, -facing), "Automatic docking must not move backward");
            Check(TryResolveAutoDriveDockSignedStep(facing, facing, .02f, .5f, out float step) && Near(step, .02f), "Forward docking step must stop exactly at the target");
            Check(!TryResolveAutoDriveDockSignedStep(facing, -facing, .02f, .5f, out _), "Dock overshoot must not request reverse correction");
            engine.autoDriveResolvedTargetStation = StationB;
            engine.testDock = true; engine.testDockDirection = facing; engine.testDockDistance = .005f;
            Check(Near(engine.AdjustDrivenSignedStep(sample, facing, true, facing, .1f, .3f), .005f), "Even sub-complete-distance docking must clamp overshoot");
            engine.autoDriveEnabled = false;
            Check(engine.ResolveRailInputAxis(true, -facing, 1, facing, sample) < 0 && engine.CanDockInDirection(facing, -facing), "Manual reverse input and docking must remain unchanged");
        }

        var drivingEngine = Engine(2, Vector2.right);
        var oppositeEngine = Engine(5, Vector2.left);
        Train.Link(drivingEngine, oppositeEngine);
        oppositeEngine.HasWaterDock = true;
        Check(drivingEngine.TestConsistWaterDocking() && oppositeEngine.WaterPipeReady,
            "The non-driving locomotive must deploy its pipe and receive water when already aligned");

        drivingEngine.HasWaterDock = true;
        Check(drivingEngine.TestConsistWaterDocking()
              && drivingEngine.WaterPipeReady
              && oppositeEngine.WaterPipeReady,
            "Both locomotives must receive water when both docks are aligned");

        drivingEngine.HasWaterDock = true;
        drivingEngine.WaterDockDelta = 0f;
        oppositeEngine.WaterDockDelta = .02f;
        drivingEngine.DockMovement = 0f;
        Check(drivingEngine.TestConsistWaterDocking()
              && drivingEngine.WaterPipeReady
              && !oppositeEngine.WaterPipeReady
              && Near(drivingEngine.DockMovement, 0f),
            "An aligned filling locomotive must not be pulled away to align the opposite engine");

        drivingEngine.HasWaterDock = false;
        oppositeEngine.WaterDockDelta = -.02f;
        drivingEngine.DockMovement = 0f;
        Check(drivingEngine.TestConsistWaterDocking()
              && drivingEngine.DockMovement < 0f,
            "The driving locomotive must move the consist to an available opposite-engine dock");

        var stationDriver = Engine(2, Vector2.right);
        var stationOpposite = Engine(5, Vector2.left);
        Train.Link(stationDriver, stationOpposite);
        stationDriver.autoDriveEnabled = true;
        stationDriver.SetTestStationDock(5f, .02f);
        Check(stationDriver.TestConsistStationDocking()
              && stationDriver.DockMovement > 0f,
            "The non-driving opposite locomotive must approach a forward-reachable station");

        stationDriver.DockMovement = 0f;
        stationDriver.SetTestStationDock(5f, 0f);
        Check(stationDriver.TestConsistStationDocking()
              && Near(stationDriver.DockMovement, 0f),
            "An opposite locomotive already at the station must dock without moving the consist");

        stationDriver.SetTestStationDock(5f, -.02f);
        Check(stationDriver.TestConsistStationDocking()
              && Near(stationDriver.DockMovement, 0f),
            "A reverse-only opposite station must reserve the dock without reversing automatic travel");

        stationDriver.autoDriveEnabled = false;
        Check(stationDriver.TestConsistStationDocking()
              && stationDriver.DockMovement < 0f,
            "Manual driving must be able to align the opposite locomotive by reversing the consist");
        RunPathTransferChecks();
        RailHandcar.RunInitialPathChecks(Check);
        RailHandcar.RunDepartureChecks(Check);
        Console.WriteLine($"PASS: {checks} train automatic-driving checks");
    }
}
