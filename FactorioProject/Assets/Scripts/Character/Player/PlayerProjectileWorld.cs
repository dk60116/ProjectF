using System.Collections.Generic;
using ProjectF.Monsters;
using UnityEngine;

namespace ProjectF.Combat
{
    // One simulation loop and reusable views. Projectiles are transient; spent ammunition is saved, not flight state.
    public sealed class PlayerProjectileWorld : MonoBehaviour
    {
        private sealed class View
        {
            internal GameObject Root;
            internal Transform Transform;
            internal MeshFilter Filter;
            internal MeshRenderer Renderer;
            internal Quaternion MeshRotation;
            internal int ReleaseFrame;
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
                    Filter = root.AddComponent<MeshFilter>(), Renderer = root.AddComponent<MeshRenderer>() };
                view.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
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
            shots.Add(new Shot { View = view, Target = target, Position = origin,
                Remaining = stats.Range, Speed = stats.Speed, Damage = stats.Damage, Threat = threat,
                SpawnFrame = Time.frameCount });
        }

        private void Update()
        {
            if (MapObjectTickManager.SimulationPaused || MapObjectTickManager.WaitingForWorldLoad
                || SaveManager.GameplayInputBlocked) return;
            for (int i = impactViews.Count-1; i >= 0; i--)
            {
                View view = impactViews[i];
                if (Time.frameCount <= view.ReleaseFrame) continue;
                view.Root.SetActive(false);
                pool.Push(view);
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
                impactViews.Add(shot.View);
            }
            else
            {
                shot.View.Root.SetActive(false);
                pool.Push(shot.View);
            }
            shots[index] = shots[shots.Count-1];
            shots.RemoveAt(shots.Count-1);
        }

        public void ClearForLoad()
        {
            for (int i = 0; i < shots.Count; i++)
            { shots[i].View.Root.SetActive(false); pool.Push(shots[i].View); }
            shots.Clear();
            for (int i = 0; i < impactViews.Count; i++)
            { impactViews[i].Root.SetActive(false); pool.Push(impactViews[i]); }
            impactViews.Clear();
        }
        private void OnDisable() => ClearForLoad();
    }
}
