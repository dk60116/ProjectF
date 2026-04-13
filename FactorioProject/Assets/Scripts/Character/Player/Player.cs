using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Player : Character
{
    private static readonly int PickHash = Animator.StringToHash("tPick");
    private const string PickStateName = "Pick";
    private const string IdleStateName = "Idle";
    private const string RunningStateName = "Running";

    [Serializable]
    public struct PlayerState
    {
        [SerializeField]
        private float miningSpeed;

        [SerializeField]
        private float loggingSpeed;

        [SerializeField]
        private float harvestRange;

        public float MiningSpeed => miningSpeed > 0f ? miningSpeed : 1f;
        public float LoggingSpeed => loggingSpeed > 0f ? loggingSpeed : 1f;
        public float HarvestRange => harvestRange > 0f ? harvestRange : 2f;
    }

    [FormerlySerializedAs("playerStauts")]
    [SerializeField]
    private PlayerState playerState;

    private int pendingPickTriggerCount;
    private bool wasPickStateActiveLastFrame;

    [SerializeField]
    private List<PlayerBag> bagList;
    [SerializeField]
    private int bagLevel;

    protected void Awake()
    {
        ApplyBagLevelVisibility();
        RefreshBagUI();
    }

    private new void Start()
    {
        base.Start();

        RefreshBagUI();
    }

    private void OnValidate()
    {
        ApplyBagLevelVisibility();
        RefreshBagUI();
    }

    public void QueuePickAnimation()
    {
        pendingPickTriggerCount++;
    }

    public void ClearQueuedPickAnimations()
    {
        pendingPickTriggerCount = 0;
    }

    public bool UpdateAnimationState(bool shouldRun)
    {
        if (animator == null)
        {
            return false;
        }

        bool isPickActive = IsPickStateActive();

        if (shouldRun && isPickActive)
        {
            InterruptPickAnimation(true);
            wasPickStateActiveLastFrame = false;
            animator.SetBool(MoveHash, true);
            return false;
        }

        bool finishedPickThisFrame = wasPickStateActiveLastFrame && !isPickActive;
        animator.SetBool(MoveHash, shouldRun && !isPickActive);

        if (pendingPickTriggerCount > 0 && !isPickActive)
        {
            animator.ResetTrigger(PickHash);
            animator.SetTrigger(PickHash);
            pendingPickTriggerCount--;
        }

        wasPickStateActiveLastFrame = IsPickStateActive();
        return finishedPickThisFrame;
    }

    public PlayerState State => playerState;

    private bool IsPickStateActive()
    {
        if (animator == null)
        {
            return false;
        }

        if (animator.GetCurrentAnimatorStateInfo(0).IsName(PickStateName))
        {
            return true;
        }

        return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(PickStateName);
    }

    private void InterruptPickAnimation(bool shouldRun)
    {
        if (animator == null)
        {
            return;
        }

        animator.ResetTrigger(PickHash);
        animator.Play(shouldRun ? RunningStateName : IdleStateName, 0, 0f);
        animator.Update(0f);
    }

    public PlayerBag GetBag()
    {
        if (bagList == null || bagList.Count == 0)
        {
            return null;
        }

        int activeBagIndex = Mathf.Clamp(bagLevel, 0, bagList.Count - 1);
        PlayerBag activeBag = bagList[activeBagIndex];
        return activeBag != null && activeBag.gameObject.activeInHierarchy ? activeBag : null;
    }

    public bool TryAddToBag(int objectId, out PortableObject targetPortableObject)
    {
        targetPortableObject = null;
        PlayerBag activeBag = GetBag();
        if (activeBag == null)
        {
            return false;
        }

        return activeBag.TryAddObject(objectId, out targetPortableObject);
    }

    private void ApplyBagLevelVisibility()
    {
        if (bagList == null || bagList.Count == 0)
        {
            return;
        }

        int activeBagIndex = Mathf.Clamp(bagLevel, 0, bagList.Count - 1);
        for (int i = 0; i < bagList.Count; i++)
        {
            PlayerBag bag = bagList[i];
            if (bag == null)
            {
                continue;
            }

            bool shouldBeVisible = i == activeBagIndex;
            if (bag.gameObject.activeSelf != shouldBeVisible)
            {
                bag.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private void RefreshBagUI()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.UIManager == null)
        {
            return;
        }

        GameManager.Instance.UIManager.BindPlayerBag(GetBag());
    }
}
