using System.Collections.Generic;
using ProjectF.Monsters;
using UnityEngine;

namespace ProjectF.Combat
{
    // One simulation loop and reusable views. Projectiles are transient; spent ammunition is saved, not flight state.
    public sealed class PlayerProjectileWorld : MonoBehaviour
    {
        private const float TrailLifetime = .18f;
        private const int TrailPointCapacity = 32;
        private sealed class View
        {
            internal GameObject Root;
            internal Transform Transform;
            internal MeshFilter Filter;
            internal MeshRenderer Renderer;
            internal LineRenderer Trail;
            internal readonly Vector3[] TrailPoints = new Vector3[TrailPointCapacity];
            internal readonly float[] TrailTimes = new float[TrailPointCapacity];
            internal int TrailHead, TrailCount;
            internal Quaternion MeshRotation;
            internal int ReleaseFrame;
            internal float ReleaseTime;
        }
        private struct Shot
        {
            internal View View;
            internal object Target;
            internal Vector3 Position, Threat;
            internal float Remaining, Speed, Damage;
            internal int SpawnFrame;
        }
        private readonly List<Shot> shots = new List<Shot>(16);
        private readonly Stack<View> pool = new Stack<View>(16);
        private readonly List<View> impactViews = new List<View>(16);
        private Material trailMaterial;

        public static bool IsDamageable(object target) => target is MonsterInstance monster
            ? monster.IsActive && monster.CurrentHealth > 0f
            : target is Animal animal && animal != null && animal.gameObject.activeInHierarchy && animal.CanBeAttacked;

        public static Vector3 AimPoint(object target, Vector3 origin)
        {
            if (target is MonsterInstance monster) return monster.ClosestCombatPoint(origin);
            var animal = (Animal)target;
            Collider body = animal.GetComponentInChildren<Collider>();
            return body != null ? body.bounds.center : animal.transform.position + Vector3.up * .35f;
        }

        public void Launch(ItemDefinition projectile, Vector3 origin, object target, WeaponShotStats stats, Vector3 threat)
        {
            View view;
            if (pool.Count > 0) view = pool.Pop();
            else
            {
                var root = new GameObject("Player Projectile");
                root.transform.SetParent(transform, false);
                view = new View { Root = root, Transform = root.transform,
                    Filter = root.AddComponent<MeshFilter>(), Renderer = root.AddComponent<MeshRenderer>(),
                    Trail = root.AddComponent<LineRenderer>() };
                view.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.Trail.sharedMaterial = GetTrailMaterial();
                view.Trail.useWorldSpace = true;
                view.Trail.startWidth = 0f;
                view.Trail.endWidth = .045f;
                view.Trail.startColor = new Color(.95f, .95f, .9f, 0f);
                view.Trail.alignment = LineAlignment.View;
                view.Trail.textureMode = LineTextureMode.Stretch;
                view.Trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.Trail.receiveShadows = false;
            }
            ClearTrail(view);
            view.Renderer.enabled = true;
            view.Filter.sharedMesh = projectile.portableMesh;
            view.Renderer.sharedMaterial = projectile.portableMat;
            view.Transform.localScale = Vector3.one * 1.25f;
            Vector3 size = projectile.portableMesh.bounds.size;
            Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right
                : size.y >= size.z ? Vector3.up : Vector3.forward;
            view.MeshRotation = Quaternion.FromToRotation(axis, Vector3.forward);
            Vector3 offset = AimPoint(target, origin) - origin;
            Vector3 direction = offset.sqrMagnitude > .000001f ? offset.normalized : Vector3.forward;
            view.Transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction) * view.MeshRotation);
            view.Root.SetActive(true);
            AppendTrail(view, origin);
            shots.Add(new Shot { View = view, Target = target, Position = origin,
                Remaining = stats.Range, Speed = stats.Speed, Damage = stats.Damage, Threat = threat,
                SpawnFrame = Time.frameCount });
        }

        private void Update()
        {
            if (MapObjectTickManager.WaitingForWorldLoad) { ClearForLoad(); return; }
            if (MapObjectTickManager.SimulationPaused
                || SaveManager.GameplayInputBlocked) return;
            for (int i = impactViews.Count-1; i >= 0; i--)
            {
                View view = impactViews[i];
                if (Time.frameCount <= view.ReleaseFrame) continue;
                view.Renderer.enabled = false;
                if (Time.time < view.ReleaseTime)
                {
                    DrawTrail(view, (view.ReleaseTime - Time.time) / TrailLifetime);
                    continue;
                }
                RecycleView(view);
                impactViews[i] = impactViews[impactViews.Count-1]; impactViews.RemoveAt(impactViews.Count-1);
            }
            for (int i = shots.Count-1; i >= 0; i--)
            {
                Shot shot = shots[i];
                if (!IsDamageable(shot.Target))
                {
                    FinishShot(i, shot, false);
                    continue;
                }
                // HUD firing can run before this Update. Render the spawn pose at least once.
                if (shot.SpawnFrame == Time.frameCount) continue;
                float distance = Mathf.Min(shot.Remaining, shot.Speed*Time.deltaTime);
                Vector3 aim = AimPoint(shot.Target, shot.Position);
                Vector3 offset = aim - shot.Position;
                bool hit = offset.sqrMagnitude <= distance * distance;
                Vector3 direction = offset.sqrMagnitude > .000001f ? offset.normalized : Vector3.forward;
                shot.Position = hit ? aim : shot.Position + direction * distance;
                shot.View.Transform.SetPositionAndRotation(shot.Position,
                    Quaternion.LookRotation(direction) * shot.View.MeshRotation);
                if (distance > 0f) AppendTrail(shot.View, shot.Position);
                if (hit)
                {
                    if (shot.Target is MonsterInstance monster) monster.TakeDamage(shot.Damage);
                    else ((Animal)shot.Target).TakeDamage(shot.Damage, shot.Threat);
                }
                if (hit || shot.Remaining <= distance)
                {
                    FinishShot(i, shot, true);
                    continue;
                }
                shot.Remaining -= distance;
                shots[i] = shot;
            }
        }

        private void FinishShot(int index, Shot shot, bool showImpact)
        {
            if (showImpact)
            {
                shot.View.ReleaseFrame = Time.frameCount;
                shot.View.ReleaseTime = Time.time + TrailLifetime;
                impactViews.Add(shot.View);
            }
            else
            {
                RecycleView(shot.View);
            }
            shots[index] = shots[shots.Count-1];
            shots.RemoveAt(shots.Count-1);
        }

        private Material GetTrailMaterial()
        {
            if (trailMaterial != null) return trailMaterial;
            Shader shader = Shader.Find("Custom/MapFocusOverlay");
            if (shader != null)
            {
                trailMaterial = new Material(shader) { name = "Arrow Trail", hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 3000 };
                trailMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
            return trailMaterial;
        }

        private static void AppendTrail(View view, Vector3 position)
        {
            int count = view.TrailCount;
            if (count > 0 && (view.TrailPoints[(view.TrailHead + count - 1) % TrailPointCapacity] - position)
                .sqrMagnitude < .000001f) return;
            // Keep the segment crossing the time cutoff so even fast, short flights have two endpoints.
            while (view.TrailCount > 1 && (view.TrailCount == TrailPointCapacity
                || view.TrailTimes[(view.TrailHead + 1) % TrailPointCapacity] < Time.time - TrailLifetime))
            {
                view.TrailHead = (view.TrailHead + 1) % TrailPointCapacity;
                view.TrailCount--;
            }
            int index = (view.TrailHead + view.TrailCount) % TrailPointCapacity;
            view.TrailPoints[index] = position;
            view.TrailTimes[index] = Time.time;
            view.TrailCount++;
            DrawTrail(view, 1f);
        }

        private static void DrawTrail(View view, float alpha)
        {
            view.Trail.positionCount = view.TrailCount;
            for (int i = 0; i < view.TrailCount; i++)
                view.Trail.SetPosition(i, view.TrailPoints[(view.TrailHead + i) % TrailPointCapacity]);
            view.Trail.endColor = new Color(.95f, .95f, .9f, .85f * Mathf.Clamp01(alpha));
            view.Trail.enabled = view.Trail.sharedMaterial != null && view.TrailCount >= 2;
        }

        private static void ClearTrail(View view)
        {
            view.TrailHead = view.TrailCount = 0;
            view.Trail.positionCount = 0;
            view.Trail.enabled = false;
        }

        private void RecycleView(View view)
        {
            ClearTrail(view);
            view.Renderer.enabled = false;
            view.Root.SetActive(false);
            pool.Push(view);
        }

        public void ClearForLoad()
        {
            for (int i = 0; i < shots.Count; i++)
                RecycleView(shots[i].View);
            shots.Clear();
            for (int i = 0; i < impactViews.Count; i++)
                RecycleView(impactViews[i]);
            impactViews.Clear();
        }
        private void OnDisable() => ClearForLoad();
        private void OnDestroy()
        {
            if (trailMaterial != null) Destroy(trailMaterial);
        }
    }
}
