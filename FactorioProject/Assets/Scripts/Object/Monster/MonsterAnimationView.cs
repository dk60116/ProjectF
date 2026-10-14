using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ProjectF.Monsters
{
    internal sealed class MonsterAnimationView : IDisposable
    {
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[5];
        private readonly float[] lengths = new float[5];
        private int current = -1;

        internal MonsterAnimationView(GameObject root, MonsterAnimations animations)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || animations == null) return;
            animator.enabled = true;
            animator.applyRootMotion = false;
            graph = PlayableGraph.Create("Monster Combat");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            AnimationPlayableOutput.Create(graph, "Monster", animator).SetSourcePlayable(mixer);
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = animations.GetClip((MonsterAnimationState)i);
                if (clip == null || clip.legacy) continue;
                lengths[i] = clip.length;
                clips[i] = AnimationClipPlayable.Create(graph, clip);
                clips[i].SetApplyFootIK(false);
                clips[i].SetApplyPlayableIK(false);
                graph.Connect(clips[i], 0, mixer, i);
            }
            graph.Play();
            SetState(MonsterAnimationState.Idle);
        }

        internal void SetState(MonsterAnimationState state, bool restart = false, float duration = 0f)
        {
            if (!graph.IsValid()) return;
            int index = (int)state;
            if (!clips[index].IsValid()) index = 0;
            if (!clips[index].IsValid() || current == index && !restart) return;
            if (current >= 0) mixer.SetInputWeight(current, 0f);
            current = index;
            mixer.SetInputWeight(current, 1f);
            clips[current].SetTime(0d);
            clips[current].SetSpeed(duration > 0f ? lengths[current] / duration : 1d);
        }

        internal void Tick(float deltaTime)
        {
            if (!graph.IsValid() || current < 0) return;
            if (current == (int)MonsterAnimationState.Idle || current == (int)MonsterAnimationState.Move)
            {
                float length = lengths[current];
                if (length > 0f && clips[current].GetTime() >= length)
                    clips[current].SetTime(clips[current].GetTime() % length);
            }
            else if (clips[current].GetTime() >= lengths[current])
            {
                clips[current].SetTime(lengths[current]);
                graph.Evaluate(0f);
                return;
            }
            graph.Evaluate(deltaTime);
        }

        public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
    }
}
