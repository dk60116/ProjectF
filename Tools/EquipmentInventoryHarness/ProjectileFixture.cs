using UnityEngine;

namespace UnityEngine
{
    public sealed class Mesh { public Bounds bounds = new() { size = new(0.3f,0.04f,0.04f) }; }
    public sealed class Material { }
    public sealed class MeshFilter { public Mesh sharedMesh; }
    public sealed class MeshRenderer { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; }
    public sealed class Animator
    {
        public void SetLayerWeight(int layer,float weight) { }
        public void CrossFadeInFixedTime(string state,float duration,int layer,float offset) { }
    }
    public struct Bounds { public Vector3 center, size; }
    public sealed class Collider
    {
        public Bounds bounds;
    }
    public static class Time { public static float time, deltaTime = .1f; public static int frameCount; }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
public static class MapObjectTickManager { public static bool SimulationPaused, WaitingForWorldLoad; }
public static class SaveManager { public static bool GameplayInputBlocked; }
public sealed class PlayerController
{
    public bool IsMounted;
    public Vector3 FacedPoint;
    public void FaceRangedTarget(Vector3 point) => FacedPoint=point;
}
public sealed class Animal : UnityEngine.MonoBehaviour
{
    public bool CanBeAttacked = true;
    public float Health = 100;
    public void TakeDamage(float damage,Vector3 threat) => Health -= damage;
}
namespace ProjectF.Monsters
{
    public sealed class MonsterInstance
    {
        public bool IsActive = true;
        public float CurrentHealth = 100;
        public Vector3 Position = new(0,.65f,5);
        public Vector3 ClosestCombatPoint(Vector3 origin) => Position;
        public void TakeDamage(float damage) => CurrentHealth -= damage;
    }
}
