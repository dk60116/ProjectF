using UnityEngine;

public partial class Player
{
    private const string UpperBodyLayerName = "UpperBody";
    private const float UpperBodyBlendDuration = .1f;
    private static readonly int UpperBodyEmptyHash = Animator.StringToHash("UpperBody.Empty");
    private Animator upperBodyAnimator;
    private RuntimeAnimatorController upperBodyController;
    private int upperBodyLayer = -1;

    private int GetUpperBodyLayer()
    {
        if (animator == null) return -1;
        if (upperBodyAnimator != animator || upperBodyController != animator.runtimeAnimatorController)
        {
            upperBodyAnimator = animator;
            upperBodyController = animator.runtimeAnimatorController;
            upperBodyLayer = animator.GetLayerIndex(UpperBodyLayerName);
        }
        return upperBodyLayer;
    }

    private bool IsAnimationStateActive(int layer, string state)
    {
        return animator != null && layer >= 0 && (animator.GetCurrentAnimatorStateInfo(layer).IsName(state)
            || animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).IsName(state));
    }

    private void UpdateUpperBodyAnimationWeight()
    {
        int layer = GetUpperBodyLayer();
        if (layer < 0) return;
        // Fade out as the action exits, not after the empty state has already taken over.
        var state = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer)
            : animator.GetCurrentAnimatorStateInfo(layer);
        bool actionActive = !handcartAnimationActive && !ridingAnimationActive && !IsDead
            && (state.IsName(PickStateName) || state.IsName("Throw") || state.IsName("Shoot"));
        animator.SetLayerWeight(layer, Mathf.MoveTowards(animator.GetLayerWeight(layer),
            actionActive ? 1f : 0f, Time.deltaTime / UpperBodyBlendDuration));
    }

    private void ResetUpperBodyActions()
    {
        if (animator == null) return;
        animator.ResetTrigger(PickHash);
        animator.ResetTrigger(ThrowHash);
        int layer = GetUpperBodyLayer();
        if (layer >= 0)
        {
            animator.Play(UpperBodyEmptyHash, layer, 0f);
            animator.SetLayerWeight(layer, 0f);
        }
        wasPickStateActiveLastFrame = false;
        PickAnimationStartedThisFrame = false;
    }
}
