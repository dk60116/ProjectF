using System.Reflection;
using UnityEngine;

static class Checks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static Raft Create()
    {
        TerrainGenerator.Active = new();
        Physics.Blocked = _ => false;
        Physics.Obstacle = new();
        var raft = new Raft();
        var collider = new BoxCollider { transform = raft.transform };
        typeof(Ship).GetField("drivingCollider", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(raft, collider);
        return raft;
    }
    private static void Drive(Ship ship, Vector3 direction, int frames = 20, Player player = null)
    {
        for (int i = 0; i < frames; i++) ship.HandleMountedInput(direction, 4, .05f, player);
    }
    static int Main()
    {
        try { Run(); Console.WriteLine($"PASS: {checks} production ship driving/boarding/dismount checks"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Run()
    {
        PlayerHUD.CheckEquippedVehicleInteraction();
        var raft = Create();
        Drive(raft, Vector3.forward);
        Check(raft.transform.position.z > 1 && raft.CurrentVehicleSignedSpeed > 0, "Forward input drives the installed raft");
        Check(raft.CurrentVehicleSignedSpeed <= raft.VehicleMaxSpeed && raft.Refreshes > 0, "Ship speed and persistence use vehicle settings");
        Drive(raft, Vector3.zero);
        var stopped = raft.transform.position;
        Drive(raft, Vector3.zero);
        Check(raft.CurrentVehicleSignedSpeed == 0 && raft.transform.position == stopped, "No input decelerates to a stable stop");
        raft.NotifyPlayerDismounted(null);
        Check(raft.CurrentVehicleSignedSpeed == 0, "Dismount resets vehicle motion");

        raft = Create();
        Drive(raft, -Vector3.forward);
        Check(raft.transform.position.z < -1 && Quaternion.Angle(raft.transform.rotation, Quaternion.identity) < .01f,
            "Reverse drives backwards without turning the raft 180 degrees");
        raft = Create();
        Drive(raft, Vector3.right, 30);
        Check(raft.transform.position.x > 1 && Quaternion.Angle(raft.transform.rotation, new Quaternion(90)) < .01f,
            "Lateral input steers gradually and then travels in the requested direction");
        Check(MathF.Abs(raft.transform.position.y) < .00001f, "Driving preserves the water plane height");
        raft = Create();
        raft.HandleMountedInput(Vector3.forward, 4, 0);
        Check(raft.transform.position == Vector3.zero && raft.CurrentVehicleSignedSpeed == 0, "Zero delta time does not advance motion");
        raft.isActiveAndEnabled = false;
        Drive(raft, Vector3.forward);
        Check(raft.transform.position == Vector3.zero, "Inactive or pooled raft cannot be driven");
        raft = Create();
        TerrainGenerator.Active.Loaded = false;
        Drive(raft, Vector3.forward);
        Check(raft.transform.position == Vector3.zero && raft.CurrentVehicleSignedSpeed == 0, "Unknown/unloaded water is not traversed");
        raft = Create();
        TerrainGenerator.Active = null;
        Drive(raft, Vector3.forward);
        Check(raft.transform.position == Vector3.zero, "Missing terrain fails safely");

        raft = Create();
        TerrainGenerator.Active.Water = _ => -1;
        Drive(raft, Vector3.forward);
        Check(raft.transform.position == Vector3.zero, "Ship cannot drive on land");
        raft = Create();
        TerrainGenerator.Active.Water = point => 1 - point.y;
        Drive(raft, Vector3.forward, 60);
        Check(raft.transform.position.z > 0 && raft.transform.position.z < .4f && raft.CurrentVehicleSignedSpeed == 0,
            "The whole hull stops at the shoreline, not only the center");
        raft = Create();
        TerrainGenerator.Active.Water = point => point.y - .3f;
        Drive(raft, Vector3.forward, 40);
        Check(raft.transform.position.z > 2, "A raft placed partly on the shore may escape into deeper water");
        raft = Create();
        Physics.Blocked = center => center.z > .4f;
        Drive(raft, Vector3.forward, 60);
        Check(raft.transform.position.z <= .4f && raft.CurrentVehicleSignedSpeed == 0, "Substeps stop at collidable obstacles");
        raft = Create();
        Physics.Blocked = _ => true;
        Physics.Obstacle.transform = raft.transform;
        Drive(raft, Vector3.forward);
        Check(raft.transform.position.z > 1, "Hull ignores its own collider");
        raft = Create();
        var player = new Player();
        Physics.Blocked = _ => true;
        Physics.Obstacle.transform = player.transform;
        Drive(raft, Vector3.forward, player: player);
        Check(raft.transform.position.z > 1, "Mounted player's body cannot block the hull");
        raft = Create();
        TerrainGenerator.Active.TryGetLoadedBlock(new(0, 1), out var occupied);
        occupied.MapObject = new object();
        Drive(raft, Vector3.forward);
        Check(raft.transform.position == Vector3.zero, "Grid-only structures block ships even without active physics colliders");

        raft = Create();
        player = new Player();
        var controller = new PlayerController();
        player.Component = controller;
        Check(!raft.TryDockPlayer(player), "Missing boarding point fails without teleporting the player");
        var far = new Transform { position = new(0, .352f, 1) };
        var near = new Transform { position = new(0, .352f, -.35f) };
        raft.AddPlayerPoint(far);
        raft.AddPlayerPoint(near);
        Check(raft.TryDockPlayer(player) && controller.Snap == near && controller.Mounted == raft,
            "Boarding reuses nearest authored PlayerPoint and existing controller mount path");
        Check(player.AnimationUpdates == 1 && raft.TryGetPlayerPointIndex(near, out int index) && index == 1,
            "Mounted state records its player point for save/load");
        Check(raft.TryDockPlayerAtPoint(player, 0) && controller.Snap == far,
            "Saved player-point index restores boarding without new save fields");
        Check(!controller.Exit(raft, out _), "No dismount into open water");
        TerrainGenerator.Active.Water = point => point.x >= 2 ? -1 : 1;
        Check(!controller.Exit(raft, out _), "Dry land within the old three-cell radius is not enough for dismount");
        raft.transform.position = new(.8f, 0, 0);
        Check(controller.Exit(raft, out var exit) && exit.x >= 2
            && raft.CanReachDismountPosition(exit, .3f), "Dismount succeeds only alongside reachable dry shore");
        raft.transform.position = Vector3.zero;
        Check(raft.CanReachDismountPosition(new(1.36f, 0, 0), .3f), "Half-cell water gap plus player clearance is reachable");
        Check(!raft.CanReachDismountPosition(new(1.38f, 0, 0), .3f), "A water gap larger than half a cell is rejected");
        Check(!raft.CanReachDismountPosition(new(.7f, 0, 0), .3f), "Landing player cannot overlap the hull");
        Check(!raft.CanReachDismountPosition(new(1.2f, 0, 1.2f), .3f), "Diagonal gaps use actual corner distance");
        raft.transform.rotation = new Quaternion(90);
        Check(raft.CanReachDismountPosition(new(1.45f, 0, 0), .3f), "Rotated hull length participates in shore reach");
        raft.transform.rotation = Quaternion.identity;
        Check(!raft.CanReachDismountPosition(new(1.45f, 0, 0), .3f), "Unrotated short side does not inherit the long-side reach");
        raft.transform.lossyScale = new(2, 1, 1);
        Check(raft.CanReachDismountPosition(new(1.9f, 0, 0), .3f), "Shore reach follows authored hull scale");
        raft.transform.lossyScale = Vector3.one;
        raft.transform.position = new(.8f, 0, 0);
        TerrainGenerator.Active.Clear = _ => false;
        Check(!controller.Exit(raft, out _), "No dismount into a structure or resource footprint");
        TerrainGenerator.Active.Clear = _ => true;
        TerrainGenerator.Active.Water = point => point.x >= 4 ? -1 : 1;
        Check(!controller.Exit(raft, out _), "Dismount never teleports across a distant shore");
        TerrainGenerator.Active.Water = _ => -1;
        Check(controller.Exit(new Train(), out _), "Existing train dismount still works on dry land");

        raft = Create();
        for (int i = 0; i < 100; i++)
        {
            raft.transform.position = Vector3.zero;
            raft.HandleMountedInput(Vector3.forward, 4, .01f);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            raft.transform.position = Vector3.zero;
            raft.HandleMountedInput(Vector3.forward, 4, .01f);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Warmed ship input/collision checks allocate no managed memory");
    }
}
