using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Rendering
{
    // Lifetime follows an installation, not a per-object MonoBehaviour callback.
    internal sealed class InstallationVisualState
    {
        private sealed class AnimatorState
        {
            internal Animator Animator;
            internal bool Suspended;
            internal bool KeepState;
        }

        private sealed class ParticleState
        {
            internal ParticleSystem Effect;
            internal bool Requested;
            internal bool Suppressed;
            internal float Speed = 1f;
        }

        internal readonly InstallationObject Owner;
        internal int Index = -1;
        internal bool Visible { get; private set; } = true;
        private readonly List<AnimatorState> animators = new List<AnimatorState>();
        private readonly List<ParticleState> particles = new List<ParticleState>();
        private bool captured;
        private Bounds localBounds;
        private Bounds worldBounds;
        private Matrix4x4 lastMatrix;
        private bool hasMatrix;
        private int layerMask;

        internal InstallationVisualState(InstallationObject owner) { Owner = owner; }

        internal void Tick(CameraRenderCulling culling, float deltaTime)
        {
            Capture();
            Matrix4x4 matrix = Owner.transform.localToWorldMatrix;
            if (!hasMatrix || !lastMatrix.Equals(matrix))
            {
                lastMatrix = matrix;
                hasMatrix = true;
                worldBounds = VirtualRenderBatchCollection.CalculateWorldBounds(localBounds, matrix);
            }
            SetVisible(culling.IsAnyLayerVisible(layerMask) && culling.Intersects(worldBounds));
            if (Visible)
                Owner.RunManagedVisualUpdate(deltaTime);
        }

        private void Capture()
        {
            if (captured || Owner == null)
                return;
            captured = true;
            Transform root = Owner.transform;
            Matrix4x4 inverse = root.worldToLocalMatrix;
            // Include a margin for moving parts and nearby shadows; never use Renderer.isVisible
            // (Scene view and other cameras would then keep simulation-side visuals alive).
            localBounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            layerMask = 1 << Owner.gameObject.layer;
            Renderer[] renderers = Owner.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<InstallationObject>() != Owner)
                    continue;
                layerMask |= 1 << renderer.gameObject.layer;
                localBounds.Encapsulate(VirtualRenderBatchCollection.CalculateWorldBounds(renderer.bounds, inverse));
            }
            localBounds.Expand(2f);
            Animator[] foundAnimators = Owner.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < foundAnimators.Length; i++)
            {
                Animator animator = foundAnimators[i];
                if (animator.GetComponentInParent<InstallationObject>() == Owner)
                    animators.Add(new AnimatorState { Animator = animator });
            }
            ParticleSystem[] effects = Owner.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < effects.Length; i++)
            {
                ParticleSystem effect = effects[i];
                if (effect.GetComponentInParent<InstallationObject>() != Owner)
                    continue;
                // A root command already controls its particle children.
                ParticleSystem parent = effect.transform.parent != null
                    ? effect.transform.parent.GetComponentInParent<ParticleSystem>() : null;
                if (parent == null || parent.GetComponentInParent<InstallationObject>() != Owner)
                    FindParticle(effect);
                ParticleSystem.MainModule main = effect.main;
                float lifetime = main.startLifetime.constantMax;
                float reach = Mathf.Max(2f, main.startSpeed.constantMax * lifetime
                    + 0.5f * Physics.gravity.magnitude * Mathf.Abs(main.gravityModifier.constantMax) * lifetime * lifetime);
                Bounds effectBounds = new Bounds(effect.transform.position, Vector3.one * reach * 2f);
                localBounds.Encapsulate(VirtualRenderBatchCollection.CalculateWorldBounds(effectBounds, inverse));
            }
        }

        private ParticleState FindParticle(ParticleSystem effect)
        {
            for (int i = 0; i < particles.Count; i++)
                if (particles[i].Effect == effect)
                    return particles[i];
            var state = new ParticleState
            {
                Effect = effect,
                Requested = effect.isEmitting && effect.main.loop,
                Speed = effect.main.simulationSpeed
            };
            particles.Add(state);
            return state;
        }

        internal void SetParticle(ParticleSystem effect, bool active, float speed, bool clear)
        {
            if (effect == null)
                return;
            ParticleState state = FindParticle(effect);
            state.Requested = active;
            state.Speed = Mathf.Max(0f, speed);
            if (!Visible)
            {
                // Clear once when necessary. Merely suppressing Play does not stop simulation.
                if (!state.Suppressed)
                    effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                state.Suppressed = true;
                return;
            }
            ApplyParticle(state, clear);
        }

        private static void ApplyParticle(ParticleState state, bool clear)
        {
            ParticleSystem effect = state.Effect;
            if (effect == null)
                return;
            state.Suppressed = false;
            ParticleSystem.MainModule main = effect.main;
            if (!Mathf.Approximately(main.simulationSpeed, state.Speed))
                main.simulationSpeed = state.Speed;
            if (state.Requested)
            {
                if (effect.gameObject.activeInHierarchy && !effect.isEmitting)
                    effect.Play(true);
            }
            else if (clear || effect.isEmitting || effect.isPaused)
            {
                effect.Stop(true, clear
                    ? ParticleSystemStopBehavior.StopEmittingAndClear
                    : ParticleSystemStopBehavior.StopEmitting);
            }
        }

        internal void SetVisible(bool visible)
        {
            if (Visible == visible)
                return;
            Visible = visible;
            if (visible && Owner != null && Owner.isActiveAndEnabled)
                Owner.RefreshManagedVisualState();
            for (int i = 0; i < animators.Count; i++)
            {
                AnimatorState state = animators[i];
                Animator animator = state.Animator;
                if (animator == null)
                    continue;
                if (!visible && animator.enabled)
                {
                    state.KeepState = animator.keepAnimatorStateOnDisable;
                    animator.keepAnimatorStateOnDisable = true;
                    if (animator.gameObject.activeInHierarchy && !animator.isInitialized)
                        animator.Update(0f);
                    animator.enabled = false;
                    state.Suspended = true;
                }
                else if (visible && state.Suspended)
                {
                    animator.enabled = true;
                    animator.keepAnimatorStateOnDisable = state.KeepState;
                    state.Suspended = false;
                    if (animator.gameObject.activeInHierarchy)
                        animator.Update(0f);
                }
            }
            for (int i = 0; i < particles.Count; i++)
            {
                ParticleState state = particles[i];
                if (state.Effect == null)
                    continue;
                if (visible)
                    ApplyParticle(state, false);
                else
                {
                    state.Effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    state.Suppressed = true;
                }
            }
        }

        internal void Release()
        {
            // Restore only Animator state owned by this culler. Do not restart pooled effects.
            for (int i = 0; i < animators.Count; i++)
            {
                AnimatorState state = animators[i];
                if (state.Animator != null && state.Suspended)
                {
                    state.Animator.enabled = true;
                    state.Animator.keepAnimatorStateOnDisable = state.KeepState;
                }
            }
            animators.Clear();
            particles.Clear();
            captured = false;
            hasMatrix = false;
            Visible = true;
        }
    }
}
