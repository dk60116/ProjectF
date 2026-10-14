using System;
using ProjectF.Monsters;
using UnityEngine;

internal static class CollisionChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }

    public static void Run()
    {
        var root = FixtureMonsterPrefab.Create();
        var capsule = root.GetComponent<CapsuleCollider>();
        var body = root.GetComponent<Rigidbody>();
        var proxy = new MonsterCollisionBody(root);
        Check(ReferenceEquals(root.GetComponent<CapsuleCollider>(), capsule)
            && ReferenceEquals(root.GetComponent<Rigidbody>(), body), "Runtime binds prefab components without adding replacements");
        Check(capsule.radius == .42f && capsule.height == 1.3f && capsule.center.x == .1f && capsule.center.y == .65f,
            "Authored collider geometry survives instance construction");
        Check(!capsule.isTrigger && root.layer == 8 && capsule.direction == 1, "Authored solid capsule and layer are retained");
        Check(body.isKinematic && !body.useGravity && body.interpolation == RigidbodyInterpolation.None
            && body.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative, "Authored ECS-compatible body settings are retained");
        var position = new Vector3(32, 0, 44);
        var rotation = Quaternion.Euler(0, 180, 0);
        proxy.ApplyPose(position, rotation);
        Check(body.position.x == 32 && body.position.z == 44 && body.rotation.YawDegrees == 180, "ECS pose reaches the physics body");
        Check(root.transform.position.x == 32 && root.transform.rotation.YawDegrees == 180, "Physics and presentation poses stay aligned");
        proxy.SetEnabled(false);
        Check(!capsule.enabled && !body.detectCollisions, "Death/disposal releases collision immediately");
        proxy.SetEnabled(true);
        Check(capsule.enabled && body.detectCollisions, "A living restored view can reactivate its proxy");
        for (int missing = 0; missing < 2; missing++)
        {
            var invalid = new GameObject();
            if (missing == 0) invalid.AddComponent<CapsuleCollider>();
            else invalid.AddComponent<Rigidbody>();
            bool rejected = false;
            try { _ = new MonsterCollisionBody(invalid); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Missing authored physics components fail explicitly");
            Check((missing == 0 ? invalid.GetComponent<Rigidbody>() == null : invalid.GetComponent<CapsuleCollider>() == null),
                "Invalid prefabs are not silently modified at runtime");
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) proxy.ApplyPose(position, rotation);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Warm collision pose updates allocate zero bytes");

        var viewRoot = FixtureMonsterPrefab.Create();
        var view = new MonsterInstance(viewRoot, new MonsterDefinition(), 1);
        var viewCapsule = viewRoot.GetComponent<CapsuleCollider>();
        var viewBody = viewRoot.GetComponent<Rigidbody>();
        var shape = view.GetBodyShape(new Vector3(30, 0, 40), Quaternion.Euler(0, 90, 0));
        Check(MathF.Abs(shape.Center.x - 30.2f) < .0001f && MathF.Abs(shape.Center.z - 39.9f) < .0001f
            && shape.Radius == .42f && shape.MinY == 0f && shape.MaxY == 1.3f,
            "ECS geometry uses authored capsule offsets, rotation, radius and vertical extent");
        Check(viewCapsule.enabled && viewBody.detectCollisions, "Production instance construction enables authored collision");
        view.ApplySimulationPose(position, rotation);
        Check(viewRoot.transform.position.x == position.x && viewBody.rotation.YawDegrees == rotation.YawDegrees,
            "Production instance forwards the ECS pose to its body");
        view.SetCurrentHealth(0f);
        Check(!viewCapsule.enabled && !viewBody.detectCollisions, "Production instance death disables collision");
        view.SetCurrentHealth(view.MaxHealth);
        Check(viewCapsule.enabled && viewBody.detectCollisions, "Production instance health restoration enables collision");
        view.Dispose();
        view.SetCurrentHealth(view.MaxHealth);
        Check(!view.IsActive && !viewCapsule.enabled && !viewBody.detectCollisions, "Disposed instances cannot reactivate collision");
        Console.WriteLine($"PASS: {checks} production monster collision proxy checks");
    }
}
