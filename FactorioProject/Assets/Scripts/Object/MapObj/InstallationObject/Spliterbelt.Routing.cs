using ProjectF.Conveyors;
using UnityEngine;

public partial class Spliterbelt
{
    [System.Serializable]
    public sealed class PersistentState
    {
        public int filterOutput;
        public int nextInput;
        public int nextOutput = 1;
        public int wheelRotationMask;
        public PersistentState Clone() => (PersistentState)MemberwiseClone();
    }

    public PersistentState CaptureSplitterState() => new PersistentState
    {
        filterOutput = (int)filterOutput,
        nextInput = routing.NextInput,
        nextOutput = routing.NextOutput,
        wheelRotationMask = wheelRotationMask
    };

    public void ApplySplitterState(PersistentState state)
    {
        filterOutput = state != null ? (FilterOutput)Mathf.Clamp(state.filterOutput, 0, 2) : FilterOutput.Disabled;
        routing.NextInput = state != null ? state.nextInput & 1 : 0;
        routing.NextOutput = state != null ? state.nextOutput & 1 : 1;
        wheelRotationMask = state != null ? state.wheelRotationMask & 3 : 0;
        ResetWheelTransition();
        RefreshCoveredBlocks();
    }

    public enum FilterOutput { Disabled, Left, Right }
    [SerializeField] private FilterOutput filterOutput;
    private SplitterRoutingPolicy routing = new SplitterRoutingPolicy { NextOutput = 1 };
    private int wheelRotationMask;
    [SerializeField, Min(0f)] private float wheelTransitionDelay = 0.15f;
    private int displayedWheelRotationMask;
    private float leftWheelTransitionTime;
    private float rightWheelTransitionTime;

    public FilterOutput SelectedFilterOutput => filterOutput;

    public override bool IsItemFilterEnabled(int itemId, int totalItemCount)
        => IsItemFilterMaskInitialized && base.IsItemFilterEnabled(itemId, totalItemCount);

    public override void SetItemFilterEnabled(int itemId, int totalItemCount, bool enabled)
    {
        if (!IsItemFilterMaskInitialized)
            ApplyItemFilterMask(new ulong[(Mathf.Max(totalItemCount, itemId + 1) + 63) / 64], true);
        if (enabled && filterOutput == FilterOutput.Disabled)
            filterOutput = FilterOutput.Left;
        base.SetItemFilterEnabled(itemId, totalItemCount, enabled);
    }

    public void SetFilterOutput(FilterOutput value)
    {
        filterOutput = value;
        RefreshCoveredBlocks();
    }

    public int GetAllowedOutputMask(int itemId)
    {
        if (filterOutput == FilterOutput.Disabled)
            return 3;
        bool selected = IsItemFilterMaskInitialized && IsItemFilterEnabled(itemId, itemId + 1);
        int filteredChannel = filterOutput == FilterOutput.Left ? 0 : 1;
        return 1 << (selected ? filteredChannel : 1 - filteredChannel);
    }

    internal bool TrySelectOutput(int input, bool leftReady, bool rightReady,
        int leftOutputs, int rightOutputs, out int output)
    {
        return routing.TrySelect(input, leftReady, rightReady, leftOutputs, rightOutputs, out output);
    }

    internal int WheelRotationMask => wheelRotationMask;

    internal int GetDisplayedWheelRotationMask(float now)
    {
        if (now >= leftWheelTransitionTime)
            displayedWheelRotationMask = (displayedWheelRotationMask & ~1) | (wheelRotationMask & 1);
        if (now >= rightWheelTransitionTime)
            displayedWheelRotationMask = (displayedWheelRotationMask & ~2) | (wheelRotationMask & 2);
        return displayedWheelRotationMask;
    }

    private void ResetWheelTransition()
    {
        displayedWheelRotationMask = wheelRotationMask;
        leftWheelTransitionTime = rightWheelTransitionTime = 0f;
    }

    internal void CommitTransfer(int input, int output)
    {
        // Save the committed target immediately; delay only its visual transition.
        // Repeated identical results must not keep postponing a pending transition.
        float now = WheelAnimationTime;
        GetDisplayedWheelRotationMask(now);
        int previousMask = wheelRotationMask;
        int sourceBit = 1 << input;
        if (input == output)
            wheelRotationMask |= sourceBit;
        else
            wheelRotationMask &= ~sourceBit;
        if (previousMask != wheelRotationMask)
        {
            float transitionTime = now + Mathf.Max(0f, wheelTransitionDelay);
            if (input == 0) leftWheelTransitionTime = transitionTime;
            else rightWheelTransitionTime = transitionTime;
        }
        routing.Commit(input, output);
    }

    protected override void OnItemFilterMaskChanged()
    {
        base.OnItemFilterMaskChanged();
        RefreshCoveredBlocks();
    }

    public override void PrepareForPool()
    {
        base.PrepareForPool();
        filterOutput = FilterOutput.Disabled;
        routing = new SplitterRoutingPolicy { NextOutput = 1 };
        wheelRotationMask = 0;
        ResetWheelTransition();
    }
}
