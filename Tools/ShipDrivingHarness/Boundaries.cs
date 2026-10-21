using UnityEngine;

// Only engine scene/physics and terrain data are substituted; Ship and vehicle motion run unchanged.
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; activeInHierarchy = active; }
    }
    public sealed class Sprite { }
    public class MonoBehaviour
    {
        public readonly Transform transform = new();
        public GameObject gameObject => transform.gameObject;
        public bool isActiveAndEnabled = true;
        public object Component;
        public T GetComponent<T>() where T : class => Component as T;
    }
    public sealed class Transform
    {
        public Vector3 position, lossyScale = Vector3.one;
        public Quaternion rotation;
        public Transform parent;
        public readonly GameObject gameObject = new();
        public Vector3 right => rotation * Vector3.right;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public bool IsChildOf(Transform root) => ReferenceEquals(this, root) || parent != null && parent.IsChildOf(root);
    }
    public record struct Vector2(float x, float y);
    public record struct Vector2Int(int x, int y)
    {
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    }
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 right => new(1, 0, 0);
        public static Vector3 forward => new(0, 0, 1);
        public float sqrMagnitude => Dot(this, this);
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this * (1 / magnitude) : zero;
        public void Set(float a, float b, float c) { x = a; y = b; z = c; }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => a * -1;
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
    }
    public readonly record struct Quaternion(float Yaw)
    {
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 direction, Vector3 up) => new(MathF.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
        private static float Delta(float a, float b) => (b - a + 540) % 360 - 180;
        public static float Angle(Quaternion a, Quaternion b) => MathF.Abs(Delta(a.Yaw, b.Yaw));
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float degrees)
            => new(a.Yaw + Math.Clamp(Delta(a.Yaw, b.Yaw), -degrees, degrees));
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float a = q.Yaw / Mathf.Rad2Deg;
            return new(v.x * MathF.Cos(a) + v.z * MathF.Sin(a), v.y, v.z * MathF.Cos(a) - v.x * MathF.Sin(a));
        }
    }
    public static class Mathf
    {
        public const float Rad2Deg = 180 / MathF.PI;
        public static float Abs(float x) => MathF.Abs(x);
        public static float Atan2(float x, float y) => MathF.Atan2(x, y);
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp(float a, float min, float max) => Math.Clamp(a, min, max);
        public static int Clamp(int a, int min, int max) => Math.Clamp(a, min, max);
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
        public static int RoundToInt(float x) => (int)MathF.Round(x);
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        public static bool Approximately(float a, float b) => MathF.Abs(a - b) < .00001f;
    }
    public class Collider { public Transform transform = new(); }
    public sealed class BoxCollider : Collider { public Vector3 center = new(0, .166f, 0), size = new(1.14f, .32f, 1.35f); }
    public readonly record struct LayerMask(int Value) { public static implicit operator LayerMask(int value) => new(value); }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static Collider Obstacle;
        public static Func<Vector3, bool> Blocked = _ => false;
        public static int Overlaps, Syncs;
        public static void SyncTransforms() => Syncs++;
        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 extents, Collider[] result,
            Quaternion rotation, LayerMask mask, QueryTriggerInteraction triggers)
        {
            Overlaps++;
            if (!Blocked(center)) return 0;
            result[0] = Obstacle;
            return 1;
        }
    }
}
namespace UnityEngine.UI { public sealed class Button { public bool interactable; } }
public interface IMapObjectTarget
{
    bool IsTargetActive { get; }
    bool AllowsFocus { get; }
    ItemDefinition BoundItemDefinition { get; }
    int ResolveItemId();
}
public class MapObject : MonoBehaviour, IMapObjectTarget
{
    public struct MapObjectStatus { public int mapSizeX, mapSizeY; }
    public MapObjectStatus Status = new() { mapSizeX = 2, mapSizeY = 2 };
    public bool IsTargetActive => isActiveAndEnabled && transform.gameObject.activeInHierarchy;
    public bool AllowsFocus { get; set; } = true;
    public bool InRange = true;
    public ItemDefinition BoundItemDefinition { get; set; }
    public int ResolveItemId() => BoundItemDefinition?.id ?? -1;
}
public partial class Vehicle : MapObject
{
    private ProjectF.Simulation.VehicleMotionState motion;
    private List<Transform> playerPoint = new();
    public int Refreshes;
    public float VehicleMaxSpeed = 1.5f;
    public virtual float EffectiveVehicleMaxSpeed => VehicleMaxSpeed;
    public float CurrentVehicleSignedSpeed => motion.SignedSpeed;
    public virtual void HandleMountedInput(Vector3 direction, float speed, float dt) { }
    public virtual void HandleMountedInput(Vector3 direction, float speed, float dt, Player player) { }
    protected float UpdateVehicleSignedSpeed(float input, float dt, float maxSpeed) => motion.Advance(input, dt, maxSpeed, 4, 4);
    protected void ResetVehicleMotion() => motion.SignedSpeed = 0;
    protected bool RefreshSingleCellRuntimePlacement(Vector3 position, int turns) { Refreshes++; return true; }
    public void AddPlayerPoint(Transform point) => playerPoint.Add(point);
}
public class Train : Vehicle { }
public sealed class Locomotive : Train { }
public sealed class Player : MonoBehaviour
{
    public bool HasRangedEquipment = true;
    public ItemDefinition SelectedEquipment = new() { icon = new() };
    public Transform BodyTransform => transform;
    public int AnimationUpdates;
    public void StopImmediateActions() { }
    public void UpdateMountedVehicleAnimation(Vehicle vehicle) => AnimationUpdates++;
    public void ClearMountedVehicleAnimation() { }
}
public partial class PlayerController
{
    private const int VehicleDismountSearchPadding = 4;
    public Transform Snap;
    public Vehicle Mounted;
    public Vehicle MountedVehicle => Snap != null ? Mounted : null;
    private Transform interactionPointSnapTarget => Snap;
    private readonly Transform transform = new();
    public static bool InputLocked;
    private static bool IsPlayerInputLocked() => InputLocked;
    public readonly List<(IMapObjectTarget Target, float Distance)> Candidates = new();
    public int InteractionButtonFocusTargetCount => Candidates.Count;
    public bool IsWithinInteractionRange(IMapObjectTarget target) => target is MapObject native && native.InRange;
    public bool IsMountedOnVehicle(Vehicle vehicle) => ReferenceEquals(Mounted, vehicle);
    public bool TryGetInteractionButtonFocusTarget(int index, out IMapObjectTarget target, out float distance)
    { (target, distance) = Candidates[index]; return true; }
    public bool TrySnapBodyToInteractionPoint(Transform point, Vehicle vehicle) { Snap = point; Mounted = vehicle; return true; }
    private TerrainGenerator ResolveTerrainGenerator() => TerrainGenerator.Active;
    private float GetPlayerCollisionRadius() => .3f;
    private Vector3 ClampRootPositionToGroundY(Vector3 point) => new(point.x, 0, point.z);
    private bool IsPlayerBlockedByWaterAtPosition(Vector3 point) => TerrainGenerator.Active.Water(new(point.x, point.z)) > 0;
    public bool Exit(Vehicle vehicle, out Vector3 position) => TryResolveClearVehicleDismountPosition(vehicle, vehicle.transform.position, out position);
}
public sealed class Block { public object MapObject; public Vector3 WorldPosition; }
public sealed class TerrainGenerator
{
    public static TerrainGenerator Active;
    public static TerrainGenerator ResolveActive() => Active;
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public Func<Vector2, float> Water = _ => 1;
    public Func<Vector3, bool> Clear = _ => true;
    public bool Loaded = true;
    public float GetWaterSurfaceScoreAtWorldPosition(Vector2 point, float[] weights) => Water(point);
    public bool IsAnimalObstaclePositionClear(Vector3 origin, Vector3 point, float radius, bool escape) => Clear(point);
    public bool TryGetLoadedBlock(Vector2Int point, out Block block)
    {
        if (!Loaded) { block = null; return false; }
        if (!Blocks.TryGetValue(point, out block))
        {
            block = new Block { WorldPosition = new(point.x, 0, point.y) };
            Blocks.Add(point, block);
        }
        return true;
    }
}
