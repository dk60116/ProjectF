using System;
using System.Collections.Generic;
using UnityEngine;

// Animator evaluation is a port. Queueing, state detection, layer weights and cancellation are production code.
namespace UnityEngine
{
    public sealed class RuntimeAnimatorController { }
    public static class Time { public static float deltaTime = .05f; }
    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float MoveTowards(float current, float target, float step) => current + Math.Clamp(target - current, -step, step);
    }
    public sealed class Transform { }
    public readonly struct AnimatorStateInfo
    {
        private readonly string name;
        public AnimatorStateInfo(string name) => this.name = name;
        public bool IsName(string value) => name == value;
    }
    public sealed class Animator
    {
        private static readonly Dictionary<string, int> hashes = new();
        public RuntimeAnimatorController runtimeAnimatorController = new();
        public readonly string[] Current = { "Idle", "Empty" }, Next = { "Idle", "Empty" };
        public readonly bool[] Transition = new bool[2];
        public readonly float[] Weight = new float[2];
        public readonly Dictionary<int, bool> Bools = new();
        public readonly HashSet<int> Triggers = new();
        public int LayerIndex = 1, LayerLookups, LastPlayedLayer = -1;
        public string LastPlayedState;
        public static int StringToHash(string value)
        {
            if (!hashes.TryGetValue(value, out int hash)) hashes.Add(value, hash = hashes.Count + 1);
            return hash;
        }
        public int GetLayerIndex(string name) { LayerLookups++; return LayerIndex; }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => new(Current[layer]);
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer) => new(Next[layer]);
        public bool IsInTransition(int layer) => Transition[layer];
        public float GetLayerWeight(int layer) => Weight[layer];
        public void SetLayerWeight(int layer, float value) => Weight[layer] = value;
        public void SetFloat(int hash, float value) { }
        public void SetFloat(int hash, float value, float damp, float dt) { }
        public void SetBool(int hash, bool value) => Bools[hash] = value;
        public void SetTrigger(int hash) => Triggers.Add(hash);
        public void ResetTrigger(int hash) => Triggers.Remove(hash);
        public void Play(string state, int layer, float time)
        { LastPlayedLayer = layer; LastPlayedState = Current[layer] = state; Transition[layer] = false; }
        public void Play(int hash, int layer, float time) => Play("Empty", layer, time);
        public void Update(float dt) { }
        public void State(int layer, string current, string next = null)
        { Current[layer] = current; Next[layer] = next ?? current; Transition[layer] = next != null; }
    }
}
public class Vehicle
{
    public float ResolveSignedSpeedRelativeToFacing(Transform facing) => 1f;
}
public sealed class Handcart : Vehicle { public float ResolvePlayerDrivenMaxSpeed(float speed) => 1f; }
public partial class Player
{
    private static readonly int MoveHash = Animator.StringToHash("bMove");
    internal Animator animator = new();
    public bool IsDead;
    public readonly Transform transform = new();
    public Transform BodyTransform => transform;
    public sealed class Stats { public float currentMoveSpeed = 2f; }
    public readonly Stats Stat = new();
    public T GetComponentInChildren<T>() where T : class => animator as T;
}

internal static class Checks
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static bool Moving(Player player) => player.animator.Bools[Animator.StringToHash("bMove")];
    private static int Main()
    {
        var player = new Player();
        var a = player.animator;
        a.State(0, "Run");
        player.QueuePickAnimation();
        player.UpdateAnimationState(true);
        Check(Moving(player) && a.Triggers.Contains(Animator.StringToHash("tPick")), "A running player may queue an upper-body pick");
        a.State(1, "Empty", "Pick");
        player.UpdateAnimationState(true);
        Check(player.PickAnimationStartedThisFrame && Moving(player), "Incoming upper-body action emits its start while legs keep running");
        Check(a.Weight[1] == .5f && a.LastPlayedLayer == -1, "Upper-body weight blends in without restarting locomotion");
        a.State(1, "Pick");
        player.UpdateAnimationState(true);
        Check(!player.PickAnimationStartedThisFrame && a.Weight[1] == 1f, "Start signal fires only once and layer reaches full weight");
        player.UpdateAnimationState(false);
        Check(!Moving(player) && a.Current[1] == "Pick", "Stopping locomotion does not interrupt the upper-body action");
        a.State(1, "Pick", "Empty");
        Check(!player.UpdateAnimationState(true) && a.Weight[1] == .5f, "Exit transition fades out without an early completion signal");
        a.State(1, "Empty");
        Check(player.UpdateAnimationState(true) && a.Weight[1] == 0f, "Completed upper-body action emits its finish and releases the mask");
        Check(!player.UpdateAnimationState(true), "Completion signal fires only once");
        Check(a.LayerLookups == 1, "Layer index is cached across frames");

        a.runtimeAnimatorController = new();
        player.UpdateAnimationState(true);
        Check(a.LayerLookups == 2, "Replacing the Animator Controller invalidates the layer cache");
        a.State(1, "Pick");
        player.QueuePickAnimation();
        a.Triggers.Clear();
        player.UpdateAnimationState(true);
        Check(!a.Triggers.Contains(Animator.StringToHash("tPick")), "Queued actions cannot restart an active swing");
        a.State(1, "Empty");
        player.UpdateAnimationState(true);
        Check(a.Triggers.Contains(Animator.StringToHash("tPick")), "Pending action is consumed after the preceding action ends");
        player.ClearQueuedPickAnimations();

        a.State(1, "Empty");
        player.QueueDiggingAnimation();
        player.UpdateAnimationState(false);
        Check(a.Triggers.Contains(Animator.StringToHash("tDigging")), "Stationary full-body digging retains its trigger");
        a.State(0, "Digging");
        player.UpdateAnimationState(false);
        Check(player.DiggingAnimationStartedThisFrame && !Moving(player), "Digging remains a full-body action on the base layer");
        player.UpdateAnimationState(true);
        Check(a.LastPlayedLayer == 0 && a.LastPlayedState == "Run" && Moving(player), "Moving cancels digging into the actual Run state");
        Check(!player.DiggingAnimationStartedThisFrame && !player.DiggingAnimationFinishedThisFrame,
            "Cancelled digging cannot be reported as a completed harvest");

        player.UpdateAnimationState(true);
        Check(player.TriggerThrowAnimation() && Moving(player), "Throwing does not force the locomotion parameter to idle");
        a.State(1, "Throw");
        player.UpdateAnimationState(true);
        Check(Moving(player) && a.Weight[1] > 0f, "Throwing has a masked upper-body pose while locomotion continues");
        a.State(1, "Throw", "Empty");
        player.UpdateAnimationState(true);
        Check(a.Weight[1] == 0f, "Throw exit also fades out the upper-body layer");

        a.State(1, "Pick");
        player.UpdateAnimationState(true);
        player.StopImmediateActions();
        Check(a.Current[1] == "Empty" && a.Weight[1] == 0f && !Moving(player), "Immediate stop clears action pose, weight and movement");
        Check(!a.Triggers.Contains(Animator.StringToHash("tPick")) && !a.Triggers.Contains(Animator.StringToHash("tThrow")),
            "Immediate stop clears both upper-body triggers");
        a.State(1, "Pick");
        player.UpdateAnimationState(true);
        player.SetRidingAnimation(true);
        Check(a.Current[1] == "Empty" && a.Weight[1] == 0f && a.Bools[Animator.StringToHash("bRiding")],
            "Mounting an animal clears the action overlay before its full-body pose");
        player.UpdateAnimationState(true);
        Check(!Moving(player) && a.Weight[1] == 0f, "Riding cannot restart a queued action or on-foot locomotion");
        player.SetRidingAnimation(false);
        a.State(1, "Pick");
        player.UpdateAnimationState(true);
        player.UpdateMountedVehicleAnimation(new Handcart());
        Check(a.Current[1] == "Empty" && a.Weight[1] == 0f && a.Bools[Animator.StringToHash("bHandcartMounted")],
            "Handcart mounting also clears the upper-body action");
        player.UpdateAnimationState(true);
        Check(!Moving(player), "Handcart input does not activate on-foot locomotion");
        player.ClearMountedVehicleAnimation();
        player.UpdateAnimationState(true);
        Check(Moving(player), "Dismount restores independent on-foot locomotion");
        player.IsDead = true;
        player.StopImmediateActions();
        player.QueuePickAnimation();
        player.UpdateAnimationState(true);
        Check(a.Weight[1] == 0f && !a.Triggers.Contains(Animator.StringToHash("tPick")), "Death cannot start an upper-body swing");

        player = new Player();
        a = player.animator;
        a.LayerIndex = -1;
        player.UpdateAnimationState(true);
        Check(Moving(player), "A controller without the optional layer does not index an invalid layer");
        a.runtimeAnimatorController = new(); a.LayerIndex = 1;
        player.UpdateAnimationState(true);
        Check(a.LayerLookups == 2, "Layer discovery recovers after a controller replacement");
        for (int i = 0; i < 10; i++) player.UpdateAnimationState(true);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) player.UpdateAnimationState(true);
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Warmed split-animation updates allocate no per-frame GC");
        player.animator = null;
        Check(!player.UpdateAnimationState(true), "A missing Animator is handled without state queries");
        Console.WriteLine($"PASS: {checks} production player split-animation checks");
        return 0;
    }
}
