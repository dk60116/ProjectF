using System;
using System.Collections;
using System.Reflection;
using ProjectF.Railway;
using UnityEngine;

static class Checks
{
    static int checks;
    static readonly FieldInfo PresenterField = typeof(TrainWorld).GetField("couplerPresenter", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo LinksField = typeof(TrainCouplerPresenter).GetField("links", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo TickMethod = typeof(TrainCouplerPresenter).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static Quaternion Yaw(float angle) => Quaternion.AngleAxis(angle, Vector3.up);
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    static bool Same(Quaternion a, Quaternion b) => MathF.Abs(Quaternion.Dot(a, b)) > 0.99999f;
    static bool Same(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
    static TrainCouplerPresenter Presenter(TrainWorld world) => (TrainCouplerPresenter)PresenterField.GetValue(world);
    static int LinkCount(TrainWorld world) => ((IList)LinksField.GetValue(Presenter(world))).Count;
    static Action Tick(TrainWorld world) => (Action)TickMethod.CreateDelegate(typeof(Action), Presenter(world));

    static Transform Mount(Transform parent, float z, float yaw) => new Transform
    { parent = parent, localPosition = new Vector3(0, 0.25f, z), localRotation = Yaw(yaw) };

    static Train Car(float z, float yaw = 0, bool front = true, bool rear = true)
    {
        var car = new Train();
        car.transform.localPosition = new Vector3(0, 0, z);
        car.transform.localRotation = Yaw(yaw);
        car.Couplers = new[] { front ? Mount(car.transform, 0.35f, 0) : null,
            rear ? Mount(car.transform, -0.35f, 180) : null };
        return car;
    }

    static TrainInstance Attach(TrainWorld world, Train car)
    {
        var instance = world.Create();
        world.SetPlacement(instance, true, world.Count);
        world.AttachView(instance, car);
        return instance;
    }

    static void Solver()
    {
        for (int root = -180; root <= 180; root += 15)
        for (int neutral = 0; neutral <= 180; neutral += 180)
        for (int target = -75; target <= 75; target += 3)
        {
            Quaternion rest = Yaw(root) * Yaw(neutral);
            Vector3 direction = Yaw(target) * (rest * Vector3.forward);
            Check(TrainCouplerPoseSolver.TrySolveYaw(rest, Vector3.zero, direction, 80, out var pose), "Nonzero direction must solve");
            Check(Vector3.Dot(pose * Vector3.forward, direction.normalized) > 0.99999f, "Yaw must aim along the mount-to-mount line");
        }
        Check(!TrainCouplerPoseSolver.TrySolveYaw(Yaw(25), Vector3.one, Vector3.one, 80, out var zero) && Same(zero, Yaw(25)), "Coincident mounts must not produce an invalid rotation");
        TrainCouplerPoseSolver.TrySolveYaw(Quaternion.identity, Vector3.zero, Vector3.back, 80, out var capped);
        Check(Vector3.Dot(capped * Vector3.forward, Vector3.forward) > 0, "Overlapping mounts must not flip a coupler 180 degrees");
        TrainCouplerPoseSolver.TrySolveYaw(Yaw(15), Vector3.zero, new Vector3(2, 9, 2), 0, out var fixedPose);
        Check(Same(fixedPose, Yaw(15)), "A zero yaw limit must preserve the neutral orientation");
        var tilt = Quaternion.AngleAxis(12, new Vector3(1, 0, 0)) * Yaw(10);
        TrainCouplerPoseSolver.TrySolveYaw(tilt, Vector3.zero, new Vector3(1, 4, 2), 80, out var tilted);
        Check(MathF.Abs((tilted * Vector3.forward).y - (tilt * Vector3.forward).y) < 0.00001f, "Yaw-only solve must preserve authored tilt");
    }

    static void Connections()
    {
        foreach (bool aFront in new[] { false, true })
        foreach (bool bFront in new[] { false, true })
        {
            var world = new TrainWorld();
            var a = Car(0);
            var b = Car(aFront ? 1 : -1, aFront == bFront ? 180 : 0);
            var ai = Attach(world, a); var bi = Attach(world, b);
            var aPivot = a.Couplers[aFront ? 0 : 1]; var bPivot = b.Couplers[bFront ? 0 : 1];
            Quaternion aRest = aPivot.localRotation, bRest = bPivot.localRotation;
            world.Connect(ai, bi, aFront, bFront);
            Check(LinkCount(world) == 1, "Both ends must share exactly one visual link");
            Action tick = Tick(world);
            for (int angle = -45; angle <= 45; angle++)
            {
                b.transform.localRotation = Yaw((aFront == bFront ? 180 : 0) + angle);
                Vector3 aRoot = a.transform.position, bRoot = b.transform.position;
                Quaternion aRotation = a.transform.rotation, bRotation = b.transform.rotation;
                tick();
                Vector3 direction = bPivot.position - aPivot.position; direction.y = 0;
                Vector3 af = aPivot.rotation * Vector3.forward, bf = bPivot.rotation * Vector3.forward;
                Check(Vector3.Dot(af, direction.normalized) > 0.999f, "First physical end must follow the curve");
                Check(Vector3.Dot(bf, direction.normalized) < -0.999f, "Second physical end must oppose the first");
                Check(Same(aRoot, a.transform.position) && Same(bRoot, b.transform.position) &&
                    Same(aRotation, a.transform.rotation) && Same(bRotation, b.transform.rotation), "Visual updates must not modify vehicle poses");
                Quaternion aBeforeReverse = aPivot.rotation, bBeforeReverse = bPivot.rotation;
                ai.Motion = -1; bi.Motion = -1;
                tick();
                Check(Same(aPivot.rotation, aBeforeReverse) && Same(bPivot.rotation, bBeforeReverse), "Reverse travel must not remap the physical ends");
            }
            Check(!world.Connect(ai, bi, aFront, bFront) && LinkCount(world) == 1, "Repeated connect must not duplicate a visual link");
            int writes = aPivot.RotationWrites + bPivot.RotationWrites;
            for (int i = 0; i < 100; i++) tick();
            Check(writes == aPivot.RotationWrites + bPivot.RotationWrites, "Stationary links must not write transforms");
            world.Disconnect(ai, bi);
            Check(LinkCount(world) == 0 && Same(aPivot.localRotation, aRest) && Same(bPivot.localRotation, bRest), "Disconnect must reset both ends immediately");
            world.Clear();
        }
    }

    static void NestedAndPooling()
    {
        var world = new TrainWorld();
        var a = Car(0, front: false, rear: false);
        var nested = new Transform { parent = a.transform, localRotation = Yaw(35) };
        var pivot = new Transform { parent = nested, localPosition = new Vector3(0, 0.25f, 0.35f), localRotation = Yaw(-35) };
        a.Couplers = new[] { pivot, pivot, null, a.transform, new Transform() };
        var b = Car(1);
        var ai = Attach(world, a); var bi = Attach(world, b);
        Check(a.CouplerRig.Front != null && a.CouplerRig.Rear == null, "Nested authored axis must classify the physical end; duplicate/invalid references must be ignored");
        world.Connect(ai, bi, true, false);
        b.transform.localPosition = new Vector3(0.3f, 0, 1);
        Tick(world)();
        Check(!Same(pivot.localRotation, Yaw(-35)), "Nested pivot must rotate in its own parent space");
        world.DetachView(ai);
        Check(LinkCount(world) == 0 && Same(pivot.localRotation, Yaw(-35)), "Detaching a view must restore its authored local rotation");
        Check(ai.ConnectionCount == 1, "Presentation detachment must not alter the graph");
        world.AttachView(ai, a);
        Check(LinkCount(world) == 1, "Reattaching must restore links even without a graph revision");
        Tick(world)();
        world.Release(ai);
        Check(LinkCount(world) == 0 && Same(pivot.localRotation, Yaw(-35)), "Release must restore both ends and remove stale bindings");
        ai = Attach(world, a);
        world.Connect(ai, bi, true, false);
        Tick(world)();
        world.Disconnect(ai, bi);
        Check(Same(pivot.localRotation, Yaw(-35)), "Pool reuse must not capture an already rotated rest pose");
        world.Clear();
    }

    static void RefreshAndMissingEnds()
    {
        var world = new TrainWorld();
        var a = Car(0); var b = Car(1); var c = Car(2);
        var ai = Attach(world, a); var bi = Attach(world, b); var ci = Attach(world, c);
        world.Connect(ai, bi, true, false);
        world.Connect(bi, ci, true, false);
        Check(LinkCount(world) == 2, "A middle car must have two independent links");
        world.SetConnectionEnd(bi, ai, true);
        world.SetConnectionEnd(bi, ci, false);
        Check(LinkCount(world) == 2, "Exchanging both middle-car ends must recover links skipped during intermediate conflicts");
        world.SetConnectionEnd(bi, ai, false);
        world.SetConnectionEnd(bi, ci, true);
        Check(LinkCount(world) == 2, "Exchanging middle-car ends back must preserve both visual links");
        c.transform.localPosition = new Vector3(0.2f, 0, 2);
        Tick(world)();
        int cWrites = c.Couplers[1].RotationWrites;
        world.Disconnect(ai, bi);
        Check(LinkCount(world) == 1 && c.Couplers[1].RotationWrites == cWrites, "Unrelated connections must not be reset or rebuilt");
        world.SetConnectionEnd(bi, ci, false);
        Check(LinkCount(world) == 1 && Same(b.Couplers[0].localRotation, Quaternion.identity), "Changing a physical end must release the old pivot");
        world.Release(bi);
        Check(LinkCount(world) == 0, "Removing a middle car must remove all incident links");
        world.Clear();

        world = new TrainWorld();
        a = Car(0); b = Car(1, front: false, rear: false);
        ai = Attach(world, a); bi = Attach(world, b);
        world.Connect(ai, bi, true, false);
        b.transform.localPosition = new Vector3(0.2f, 0, 1);
        Tick(world)();
        Check(LinkCount(world) == 1 && !Same(a.Couplers[0].localRotation, Quaternion.identity), "A missing partner mesh must still provide a stable body-end target");
        world.Clear();
    }

    static void VisibilityAndAllocations()
    {
        var world = new TrainWorld();
        var a = Car(0); var b = Car(1);
        var ai = Attach(world, a); var bi = Attach(world, b);
        world.Connect(ai, bi, true, false);
        Action tick = Tick(world);
        Camera.main = new Camera(); GeometryUtility.Visible = true;
        tick();
        int writes = a.Couplers[0].RotationWrites;
        GeometryUtility.Visible = false;
        b.transform.localPosition = new Vector3(0.2f, 0, 1);
        tick();
        Check(writes == a.Couplers[0].RotationWrites, "Offscreen moving links must defer visual writes");
        GeometryUtility.Visible = true;
        tick();
        Check(writes < a.Couplers[0].RotationWrites, "Returning to view must update a previously deferred stationary pose");
        MapObjectTickManager.WaitingForWorldLoad = true;
        writes = a.Couplers[0].RotationWrites;
        b.transform.localPosition = new Vector3(-0.2f, 0, 1);
        tick();
        Check(writes == a.Couplers[0].RotationWrites, "World-load suspension must not apply partial poses");
        MapObjectTickManager.WaitingForWorldLoad = false;
        tick();
        for (int i = 0; i < 1000; i++) tick();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            b.transform.localPosition = new Vector3(i % 2 == 0 ? 0.2f : -0.2f, 0, 1);
            tick();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Warm moving-link loop must allocate zero managed bytes; got " + allocated);
        world.Clear();
        Check(Same(a.Couplers[0].localRotation, Quaternion.identity), "Clearing the world must restore the resting pose");
        Camera.main = null;
    }

    static void LongCarsAndRestoration()
    {
        var world = new TrainWorld();
        var a = Car(0); var b = Car(2, 25);
        a.FrontReach = 1.5f;
        a.Couplers[0].localPosition = new Vector3(0, 0.25f, 1.35f);
        var container = new Transform
        {
            localPosition = new Vector3(10, 0, -15), localRotation = Yaw(127),
            localScale = new Vector3(1.25f, 1.25f, 1.25f)
        };
        a.transform.parent = b.transform.parent = container;
        var ai = world.Create(); var bi = world.Create();
        world.Connect(ai, bi, true, false);
        world.AttachView(bi, b);
        Check(LinkCount(world) == 0, "A graph restored before both views must not create an incomplete pair");
        world.AttachView(ai, a);
        Check(LinkCount(world) == 1, "Reversed view registration order must recover a preexisting connection");
        Tick(world)();
        Vector3 direction = b.Couplers[1].position - a.Couplers[0].position;
        direction.y = 0;
        Check(Vector3.Dot(a.Couplers[0].rotation * Vector3.forward, direction.normalized) > 0.999f &&
            Vector3.Dot(b.Couplers[1].rotation * Vector3.forward, direction.normalized) < -0.999f,
            "Mixed vehicle lengths must use authored mounts under rotated and scaled world parents");
        container.localRotation = Yaw(-63);
        Tick(world)();
        direction = b.Couplers[1].position - a.Couplers[0].position;
        direction.y = 0;
        Check(Vector3.Dot(a.Couplers[0].rotation * Vector3.forward, direction.normalized) > 0.999f,
            "Parent movement must invalidate cached world poses");
        world.Clear();
    }

    static void Main()
    {
        Solver(); Connections(); NestedAndPooling(); RefreshAndMissingEnds(); VisibilityAndAllocations(); LongCarsAndRestoration();
        Console.WriteLine("TrainCouplerHarness: " + checks + " checks passed.");
    }
}
