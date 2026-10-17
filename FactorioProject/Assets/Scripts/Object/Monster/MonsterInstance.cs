using System;
using ProjectF.Presentation;
using ProjectF.Simulation.Monsters;
using UnityEngine;

namespace ProjectF.Monsters
{
    // Presentation/selection identity; no per-monster MonoBehaviour or simulation tick.
    public sealed class MonsterInstance : IDisposable
    {
        private readonly Renderer[] renderers;
        private readonly Renderer outlineRenderer;
        private readonly MonsterCollisionBody collisionBody;
        private WorldHealthBarView healthBar;
        private float healthBarHeight;
        private bool disposed;

        public GameObject Root { get; }
        internal int RootInstanceId { get; }
        public MonsterArchetypeData Data { get; }
        public Sprite Icon { get; }
        public string DisplayName { get; }
        public float MaxHealth => Data.Stats.MaxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsActive => !disposed && Root != null && Root.activeInHierarchy;

        public MonsterInstance(GameObject root, MonsterDefinition definition, int variantId)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (definition == null || !definition.TryGetVariant(variantId, out MonsterVariant variant)
                || !definition.TryGetRuntimeData(variantId, out MonsterArchetypeData data))
                throw new ArgumentException("A valid monster definition and variant are required.");
            Root = root;
            RootInstanceId = root.GetInstanceID();
            Data = data;
            Icon = variant.icon;
            DisplayName = definition.MonsterName + " " + variant.variantName;
            CurrentHealth = MaxHealth;
            renderers = root.GetComponentsInChildren<Renderer>(true);
            collisionBody = new MonsterCollisionBody(root);
            foreach (Renderer renderer in renderers)
            {
                if (outlineRenderer == null || renderer is SkinnedMeshRenderer) outlineRenderer = renderer;
                if (renderer is SkinnedMeshRenderer) break;
            }
            MonsterFocusWorld.Register(this);
        }

        public void SetCurrentHealth(float health)
        {
            if (float.IsNaN(health)) return;
            CurrentHealth = Mathf.Clamp(health, 0f, MaxHealth);
            collisionBody.SetEnabled(!disposed && CurrentHealth > 0f);
            if (CurrentHealth <= 0f || disposed) healthBar?.HideImmediately();
        }

        public float TakeDamage(float damage)
        {
            if (!IsActive || CurrentHealth <= 0f || MapObjectTickManager.SimulationPaused
                || MapObjectTickManager.WaitingForWorldLoad || SaveManager.GameplayInputBlocked) return 0f;
            var health = new CombatHealth(MaxHealth, CurrentHealth);
            float applied = health.Damage(damage);
            if (applied > 0f) SetCurrentHealth(health.Current);
            return applied;
        }

        public Vector3 ClosestCombatPoint(Vector3 origin)
        {
            MonsterBodyShape shape = collisionBody.CurrentShape;
            Vector3 offset = origin - shape.Center;
            offset.y = 0f;
            float distance = offset.magnitude;
            Vector3 point = distance > shape.Radius ? shape.Center + offset * (shape.Radius / distance) : origin;
            point.y = Mathf.Clamp(origin.y, shape.MinY, shape.MaxY);
            return point;
        }

        internal void ApplySimulationPose(Vector3 position, Quaternion rotation) => collisionBody.ApplyPose(position, rotation);
        internal bool HasCollision => IsActive && CurrentHealth > 0f && collisionBody.IsEnabled;
        internal MonsterBodyShape GetBodyShape(Vector3 position, Quaternion rotation) => collisionBody.GetShape(position, rotation);

        internal void RefreshHealthBar(Transform parent, Camera camera)
        {
            bool alive = IsActive && CurrentHealth > 0f;
            if (healthBar == null && alive && CurrentHealth < MaxHealth) EnsureHealthBar(parent);
            if (healthBar == null) return;
            healthBar.Refresh(alive, CurrentHealth / MaxHealth,
                Root != null ? collisionBody.CurrentShape.Center : Vector3.zero, healthBarHeight, camera);
        }

        internal void NotifyAttackAnimationStarted(Transform parent, Camera camera)
        {
            if (!IsActive || CurrentHealth <= 0f) return;
            EnsureHealthBar(parent);
            healthBar.NotifyAttackAnimationStarted(true, CurrentHealth / MaxHealth);
            RefreshHealthBar(parent, camera);
        }

        private void EnsureHealthBar(Transform parent)
        {
            if (healthBar != null) return;
            var shape = collisionBody.CurrentShape;
            float top = shape.MaxY;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled)
                    top = Mathf.Max(top, renderers[i].bounds.max.y);
            healthBarHeight = Mathf.Max(.5f, top - shape.Center.y + .25f);
            healthBar = new WorldHealthBarView(parent, "Monster Health Bar");
        }

        internal bool TryRaycast(Ray ray, float maxDistance, out float distance)
        {
            distance = float.MaxValue;
            if (!IsActive) return false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.bounds.IntersectRay(ray, out float hit) && hit >= 0f
                    && hit <= maxDistance && hit < distance) distance = hit;
            }
            return distance < float.MaxValue;
        }

        public void SetHoverOutline(bool visible)
        {
            if (visible && IsActive) AnimalScreenSpaceOutline.ShowHovered(outlineRenderer);
            else AnimalScreenSpaceOutline.HideHovered(outlineRenderer);
        }

        public void SetFocusedOutline(bool visible)
        {
            if (visible && IsActive) AnimalScreenSpaceOutline.ShowFocused(outlineRenderer);
            else AnimalScreenSpaceOutline.HideFocused(outlineRenderer);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            healthBar?.Dispose();
            healthBar = null;
            collisionBody.SetEnabled(false);
            SetHoverOutline(false);
            SetFocusedOutline(false);
            MonsterFocusWorld.Unregister(this);
        }
    }

    // PhysX is a collision/query proxy; the ECS world remains the only movement owner.
    internal sealed class MonsterCollisionBody
    {
        private readonly CapsuleCollider capsule;
        private readonly Rigidbody body;
        internal bool IsEnabled => capsule != null && capsule.enabled && !capsule.isTrigger && body != null && body.detectCollisions;
        internal MonsterBodyShape CurrentShape => GetShape(body.position, body.rotation);
        internal MonsterBodyShape GetShape(Vector3 position, Quaternion rotation) => MonsterBodyShape.Read(capsule, position, rotation);

        internal MonsterCollisionBody(GameObject root)
        {
            capsule = root.GetComponent<CapsuleCollider>();
            body = root.GetComponent<Rigidbody>();
            if (capsule == null || body == null)
                throw new ArgumentException("Monster prefabs require a CapsuleCollider and Rigidbody on the root.", nameof(root));
            SetEnabled(true);
        }

        internal void ApplyPose(Vector3 position, Quaternion rotation)
        {
            if (body == null) return;
            body.position = position;
            body.rotation = rotation;
        }

        internal void SetEnabled(bool enabled)
        {
            if (capsule != null) capsule.enabled = enabled;
            if (body != null) body.detectCollisions = enabled;
        }
    }

    internal readonly struct MonsterBodyShape
    {
        internal readonly Vector3 Center;
        internal readonly float Radius, MinY, MaxY;
        internal MonsterBodyShape(Vector3 center, float radius, float minY, float maxY)
        { Center = center; Radius = radius; MinY = minY; MaxY = maxY; }
        internal MonsterBodyShape Translated(Vector3 delta) => new MonsterBodyShape(Center + delta, Radius, MinY + delta.y, MaxY + delta.y);
        internal bool OverlapsHeight(in MonsterBodyShape other) => MinY < other.MaxY && MaxY > other.MinY;

        internal static MonsterBodyShape Read(CapsuleCollider capsule, Vector3 position, Quaternion rotation)
        {
            Vector3 scale = capsule.transform.lossyScale;
            Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
            // Monster and player authoring use upright capsules; preserve their actual root offsets.
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = Mathf.Max(radius, capsule.height * Mathf.Abs(scale.y) * .5f);
            return new MonsterBodyShape(center, radius, center.y - halfHeight, center.y + halfHeight);
        }
    }
}
