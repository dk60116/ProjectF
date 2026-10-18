using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public sealed class Mesh { public Bounds bounds = new() { size = new(0.3f,0.04f,0.04f) }; }
    public sealed class Material
    {
        public string name;
        public HideFlags hideFlags;
        public int renderQueue;
        public Shader Shader;
        public float DepthTest;
        public bool Destroyed;
        public Material() { }
        public Material(Shader shader) { Shader = shader; }
        public void SetFloat(string property, float value) { DepthTest = value; }
    }
    public sealed class Shader { public string name; public static Shader Find(string name) => new() { name = name }; }
    public enum HideFlags { HideAndDontSave }
    public readonly record struct Color(float r, float g, float b, float a)
    {
        public static Color white => new(1,1,1,1);
    }
    public static class Object
    {
        public static void Destroy(object value)
        {
            if (value is Material material) material.Destroyed = true;
            if (value is GameObject root) root.Destroyed = true;
        }
    }
    public sealed class MeshFilter { public Mesh sharedMesh; }
    public sealed class MeshRenderer
    {
        public Material sharedMaterial;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public bool enabled = true;
    }
    public enum LineAlignment { View }
    public enum LineTextureMode { Stretch }
    public sealed class LineRenderer
    {
        public Material sharedMaterial;
        public bool enabled = true, receiveShadows, useWorldSpace, loop;
        public int sortingOrder, SetPositionCalls;
        public float startWidth, endWidth;
        public Color startColor, endColor;
        public LineAlignment alignment;
        public LineTextureMode textureMode;
        public Rendering.ShadowCastingMode shadowCastingMode;
        public readonly List<Vector3> Positions = new();
        public int positionCount
        {
            get => Positions.Count;
            set
            {
                while (Positions.Count < value) Positions.Add(default);
                if (Positions.Count > value) Positions.RemoveRange(value, Positions.Count - value);
            }
        }
        public void SetPosition(int index, Vector3 position) { Positions[index] = position; SetPositionCalls++; }
    }
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
    public static class Time { public static float time, deltaTime = .1f, fixedDeltaTime = .02f; public static int frameCount; }
}
namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off }
    public enum CompareFunction { LessEqual = 4 }
}
public static class MapObjectTickManager { public static bool SimulationPaused, WaitingForWorldLoad; }
public static class SaveManager { public static bool GameplayInputBlocked; }
public partial class PlayerController : UnityEngine.MonoBehaviour
{
    public bool IsMounted;
    public bool IsNooseThrowInFlight;
    private Player player;
    private Transform interactionPointSnapTarget;
    private Vector3 pendingMoveDirection, pendingFacingDirection;
    private bool hasPendingFacingDirection;
    internal float MoveSpeed = 4f;
    internal int CancelledKnife, CancelledResource, CancelledCorpse, CancelledDigging, CancelledPlanting;
    private bool IsPlayerInputLocked() => SaveManager.GameplayInputBlocked || GameManager.TextInputFocused
        || GameManager.Instance.PlayerInteractionLocked;
    private void CancelKnifeAttack() => CancelledKnife++;
    private void CancelActiveResourceHarvest() => CancelledResource++;
    private void CancelActiveCorpseHarvest() => CancelledCorpse++;
    private void CancelPitchforkDigging() => CancelledDigging++;
    private void CancelSeedPlanting() => CancelledPlanting++;
    private void RotateBodyTowards(Vector3 direction) { }
    private float GetCurrentOnFootMoveSpeed() => MoveSpeed;
    internal void BindRangedForTest(Player value) => player = value;
    internal bool ApproachRangedForTest(out Vector3 direction) => TryGetRangedAttackApproachDirection(out direction);
    internal bool HasRangedRequestForTest => currentRangedAttackTarget != null;
    internal Vector3 PendingMoveForTest { get => pendingMoveDirection; set => pendingMoveDirection = value; }
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
