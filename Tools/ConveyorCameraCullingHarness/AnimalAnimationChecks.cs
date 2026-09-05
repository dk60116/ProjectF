using UnityEngine;

namespace UnityEngine
{
    // Scene boundary: disabling freezes playback; dropping retained state clears the pose.
    public struct AnimatorStateInfo { public int shortNameHash; }
    public class Animator
    {
        private bool componentEnabled = true;
        public bool enabled
        {
            get => componentEnabled;
            set { componentEnabled = value; if (!value && !keepAnimatorStateOnDisable) PoseTime = 0f; }
        }
        public bool keepAnimatorStateOnDisable;
        public bool isInitialized;
        public bool isActiveAndEnabled => enabled;
        public float speed = 1.7f, PoseTime = 0.4f;
        public int UpdateCalls, ParameterWrites, LastState;
        public float UpdatedDelta;
        public void Update(float delta) { UpdateCalls++; UpdatedDelta = delta; isInitialized = true; PoseTime += delta; }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) => new() { shortNameHash = 1 };
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer) => default;
        public bool IsInTransition(int layer) => false;
        public void SetTrigger(int hash) => ParameterWrites++;
        public void SetInteger(int hash, int value) { ParameterWrites++; LastState = value; }
    }
}
public partial class AnimalAnimationProbe
{
    public Animator anim = new();
    public bool IsAlive = true;
    private bool behaviorAnimationSuspended, animatorStateRetentionBeforeSuspend, aiAnimationStateInitialized, wakeFromRestRequested;
    private float lastAIAnimationSpeed, lastAIAnimationPlaybackScale;
    private int lastAIAnimationFlags;
    private const int DeathAnimationState = 6, FleeAnimationState = 8, WalkAnimationState = 12;
    private const int IdleStateHash = 1, DeathStateHash = 6, StateHash = 100, ResetHash = 101;
    private const int LocomotionPlaybackHash = 102, SpeedHash = 103, IsEatingHash = 104, IsDrinkingHash = 105, IsRestingHash = 106, IsFleeingHash = 107;
    public int ParameterQueries, WakeCalls;
    private T GetComponent<T>() where T : class => anim as T;
    private void RefreshLocomotionAnimatorBaseSpeeds() => ParameterQueries++;
    private float ResolveLocomotionPlaybackScale(float scale, bool running) => scale;
    private void EnsureAnimatorParameterCache() => ParameterQueries++;
    private void SetAnimatorFloatIfAvailable(int hash, float value, int mask) => anim.ParameterWrites++;
    private void SetAnimatorBoolIfAvailable(int hash, bool value, int mask) => anim.ParameterWrites++;
    private void SwitchAnimation(int state) => anim.SetInteger(StateHash, state);
    private void ClearFocusVisuals() { }
    public void WakeFromRest() { if (anim.enabled) WakeCalls++; }
    public void Disable() => OnDisable();
}
public partial class AnimalControllerProbe
{
    public AnimalAnimationProbe animal = new();
    private bool executionActive;
    public bool IsExternallyControlled, waitingForStandUp, movingToActivity, hasTarget;
    public AnimalAIState currentState;
    public int ScheduleResets, PresentationResets;
    private void ResetScheduledTick() => ScheduleResets++;
    private void ResetPresentation() => PresentationResets++;
    private void SnapToSimulationPose() { }
    private static bool IsNightTime() => true;
    public void Animate(float speed = 0f) => ApplyAnimation(speed);
}
public static class AnimalAnimationChecks
{
    public static void Check()
    {
        var controller = new AnimalControllerProbe();
        var animal = controller.animal;
        controller.SetBehaviorExecutionActive(false);
        Checks.Require(!animal.anim.enabled, "initial inactive AI suspends animation even without an active-to-inactive change");
        Checks.Require(animal.anim.UpdateCalls == 1 && animal.anim.UpdatedDelta == 0f,
            "distant spawn initializes a pose without advancing animation time");
        controller.SetBehaviorExecutionActive(false); controller.Animate();
        Checks.Require(animal.anim.UpdateCalls == 1 && animal.ParameterQueries == 0 && animal.anim.ParameterWrites == 0,
            "repeated dormant ticks neither evaluate nor query/write animation state");
        controller.SetBehaviorExecutionActive(true);
        Checks.Require(animal.anim.enabled && !animal.anim.keepAnimatorStateOnDisable && animal.anim.speed == 1.7f && animal.anim.PoseTime == 0.4f,
            "AI reactivation restores prior pose, playback speed and state-retention setting");
        controller.Animate();
        Checks.Require(animal.anim.enabled && animal.anim.ParameterWrites > 0 && animal.anim.LastState == 0,
            "active stationary Idle animals retain normal animation");
        controller.currentState = AnimalAIState.Eat; controller.Animate();
        Checks.Require(animal.anim.enabled && animal.anim.LastState == 11, "active feeding continues");
        controller.currentState = AnimalAIState.Rest; controller.Animate();
        Checks.Require(animal.anim.enabled && animal.anim.LastState == 16, "active resting continues");
        controller.SetBehaviorExecutionActive(false);
        Checks.Require(!animal.anim.enabled && controller.ScheduleResets == 1 && controller.PresentationResets == 1,
            "pause retains existing scheduler/presentation reset behavior and stops Animator");
        controller.waitingForStandUp = true;
        controller.SetBehaviorExecutionActive(true);
        Checks.Require(animal.anim.enabled && animal.WakeCalls == 1, "resuming paused stand-up work requests wake after Animator resumes");
        controller.SetBehaviorExecutionActive(true);
        Checks.Require(animal.WakeCalls == 1, "unchanged active state does not restart wake transitions");

        controller.SetBehaviorExecutionActive(false);
        controller.IsExternallyControlled = true; controller.Animate(2f);
        controller.SetBehaviorExecutionActive(false);
        Checks.Require(animal.anim.enabled && animal.anim.LastState == 12,
            "rider/leash/draft control plays animation independently of AI pause");
        controller.IsExternallyControlled = false; controller.Animate();
        Checks.Require(!animal.anim.enabled, "releasing external control restores dormant animation suspension");
        animal.IsAlive = false; controller.Animate();
        Checks.Require(animal.anim.enabled && animal.anim.LastState == 6, "death of a dormant animal resumes its death animation");

        var disabledElsewhere = new AnimalAnimationProbe();
        disabledElsewhere.anim.enabled = false;
        disabledElsewhere.SetBehaviorAnimationActive(false); disabledElsewhere.SetBehaviorAnimationActive(true);
        Checks.Require(!disabledElsewhere.anim.enabled, "AI suspension never enables an Animator disabled by another owner");
        var pooled = new AnimalAnimationProbe();
        pooled.anim.keepAnimatorStateOnDisable = true;
        pooled.SetBehaviorAnimationActive(false); pooled.Disable();
        Checks.Require(pooled.anim.enabled && pooled.anim.keepAnimatorStateOnDisable,
            "object disable releases owned suspension and preserves original state-retention preference");
    }
}
