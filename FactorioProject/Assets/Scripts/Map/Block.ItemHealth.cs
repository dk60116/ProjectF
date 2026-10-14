using System.Collections.Generic;

public partial class Block
{
    private static readonly int[] AllHealthConveyorLanes = { 0, 1, 2, 3 };
    private Dictionary<int, float> conveyorInstallationHealth;

    internal void ForgetDestroyedInfrastructure(IMapObjectTarget target)
    {
        if (ReferenceEquals(runtimeConveyorRecord, target)) runtimeConveyorRecord = null;
        if (ReferenceEquals(runtimeConveyorRecordOverride, target)) runtimeConveyorRecordOverride = null;
        if (ReferenceEquals(runtimePipeRecord, target)) runtimePipeRecord = null;
    }

    internal void DiscardInstallationContents(bool conveyor)
    {
        if (conveyor)
        {
            ClearRuntimeConveyorItemLanes(AllHealthConveyorLanes);
            return;
        }
        ClearDeferredOutputs();
        for (int i = 0; i < inputAreaCenterStack.Count; i++) ReleaseFloorObject(inputAreaCenterStack[i]);
        inputAreaCenterStack.Clear();
        NotifyRuntimeItemStackChanged();
    }

    private float GetConveyorInstallationHealth(int lane)
    {
        if (TryReadBeltJobLane(lane, out var native)) return native.InstallationHealth;
        if (ReadTransportLane(lane, out var transported, out _)) return transported.InstallationHealth;
        var portable = GetConveyorPortableObjectAtLane(lane);
        if (portable != null) return portable.InstallationHealth;
        return conveyorInstallationHealth != null && conveyorInstallationHealth.TryGetValue(lane, out var health) ? health : 0f;
    }

    private void SetConveyorInstallationHealth(int lane, float health)
    {
        GetConveyorPortableObjectAtLane(lane)?.SetInstallationHealth(health);
        if (health > 0f)
        {
            conveyorInstallationHealth ??= new Dictionary<int, float>(1);
            conveyorInstallationHealth[lane] = health;
        }
        else conveyorInstallationHealth?.Remove(lane);
    }

}

internal static class PortableItemHealthMetadata
{
    internal static float Read(IReadOnlyList<PortableItemHealthSaveState> source, int index)
    {
        for (int i = 0; source != null && i < source.Count; i++)
            if (source[i].index == index) return source[i].health;
        return 0f;
    }

    internal static void CopyRange(IReadOnlyList<PortableItemHealthSaveState> source, int first, int last,
        int destinationOffset, ref List<PortableItemHealthSaveState> destination)
    {
        for (int i = 0; source != null && i < source.Count; i++)
            if (source[i].index >= first && source[i].index <= last)
            {
                destination ??= new List<PortableItemHealthSaveState>();
                destination.Add(new PortableItemHealthSaveState
                { index = destinationOffset + source[i].index - first, health = source[i].health });
            }
    }
}
