using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }
    public sealed class RectTransform
    {
        public GameObject gameObject;
        public RectTransform parent;
        public Vector3 localPosition;
        public Vector3 position => parent != null ? parent.position + localPosition : localPosition;
        public int Sibling;
        public int GetSiblingIndex() => Sibling;
        public Vector3 InverseTransformPoint(Vector3 world) => world - position;
    }
    public sealed class CanvasGroup { public bool interactable = true, blocksRaycasts = true; }
}
public sealed class HUDButtonHoverTween
{
    public int Resets;
    public void ResetHoverImmediate(bool rebuild) => Resets++;
}
namespace DG.Tweening
{
    // Timeline and transform interpolation are ports, not a replacement for DOTween's easing engine.
    public enum Ease { OutCubic, InCubic }
    public abstract class Tween
    {
        public float Duration;
        public bool Killed, Finished;
        public Ease Ease;
        public Tween SetEase(Ease ease) { Ease = ease; return this; }
        public abstract void At(float time);
        public virtual void Kill() => Killed = true;
    }
    public sealed class MoveTween : Tween
    {
        private readonly RectTransform target;
        private readonly Vector3 destination;
        private Vector3 start;
        private bool started;
        public MoveTween(RectTransform target, Vector3 destination, float seconds)
        { this.target = target; this.destination = destination; Duration = seconds; }
        public override void At(float time)
        {
            if (Killed || Finished || time < 0f) return;
            if (!started) { started = true; start = target.localPosition; }
            target.localPosition = Vector3.Lerp(start, destination, Math.Clamp(time / Duration, 0f, 1f));
            if (time >= Duration) Finished = true;
        }
    }
    internal sealed class CallbackTween : Tween
    {
        private readonly Action callback;
        internal CallbackTween(Action callback) => this.callback = callback;
        public override void At(float time)
        { if (Killed || Finished || time < 0f) return; Finished = true; callback(); }
    }
    public sealed class Sequence : Tween
    {
        public readonly List<(float Start, Tween Child)> Items = new();
        public bool IndependentUpdate;
        private Action completed, killed;
        private float elapsed;
        public Sequence SetUpdate(bool independent) { IndependentUpdate = independent; return this; }
        public Sequence AppendCallback(Action callback) => Append(new CallbackTween(callback));
        public Sequence Append(Tween child) => Insert(Duration, child);
        public Sequence Insert(float at, Tween child)
        { Items.Add((at, child)); Duration = Math.Max(Duration, at + child.Duration); return this; }
        public Sequence OnComplete(Action callback) { completed = callback; return this; }
        public Sequence OnKill(Action callback) { killed = callback; return this; }
        public void Advance(float delta) { elapsed += delta; At(elapsed); }
        public void Complete() => At(Duration + .001f);
        public override void At(float time)
        {
            if (Killed || Finished) return;
            foreach (var item in Items) item.Child.At(time - item.Start);
            if (time >= Duration) { Finished = true; completed?.Invoke(); Kill(); }
        }
        public override void Kill()
        { if (Killed) return; Killed = true; foreach (var item in Items) item.Child.Kill(); killed?.Invoke(); }
    }
    public static class DOTween { public static Sequence Sequence() => new(); }
    public static class ShortcutExtensions
    {
        public static Tween DOLocalMove(this RectTransform rect, Vector3 position, float duration) => new MoveTween(rect, position, duration);
    }
}
