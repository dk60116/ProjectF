public partial class Block
{
    public int ClearRuntimeDroppedFloorItems()
    {
        EnsureFloorObjectsInitialized();
        int clearedCount = 0;
        for (int stackIndex = 0; stackIndex < floorStacks.Count; stackIndex++)
        {
            var stack = floorStacks[stackIndex];
            if (stack == null || stack.Count <= 0)
            {
                continue;
            }

            for (int itemIndex = 0; itemIndex < stack.Count; itemIndex++)
            {
                PortableObject portableObject = stack[itemIndex];
                if (portableObject == null)
                {
                    continue;
                }

                if (portableObject.ItemId >= 0)
                {
                    clearedCount++;
                }

                ReleaseFloorObject(portableObject);
            }

            stack.Clear();
        }

        if (clearedCount > 0)
        {
            NotifyRuntimeItemStackChanged();
        }

        return clearedCount;
    }

    public int ClearRuntimeInputOutputAreaItems()
    {
        EnsureFloorObjectsInitialized();
        int clearedCount = 0;
        for (int itemIndex = 0; itemIndex < inputAreaCenterStack.Count; itemIndex++)
        {
            PortableObject portableObject = inputAreaCenterStack[itemIndex];
            if (portableObject == null)
            {
                continue;
            }

            if (portableObject.ItemId >= 0)
            {
                clearedCount++;
            }

            ReleaseFloorObject(portableObject);
        }

        inputAreaCenterStack.Clear();
        if (clearedCount > 0)
        {
            NotifyRuntimeItemStackChanged();
        }

        return clearedCount;
    }

    public int ClearRuntimeConveyorItems()
    {
        ReleaseConveyorTransport();
        EnsureFloorObjectsInitialized();
        CleanupConveyorStack();
        if (!IsConveyorStackingEnabled())
        {
            return 0;
        }

        TerrainGenerator activeTerrain = TerrainGenerator.Active;
        int clearedCount = 0;
        int laneCount = GetConveyorLaneCount();
        for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
        {
            bool hadItem = HasConveyorItemAtLane(laneIndex);
            PortableObject portableObject = GetConveyorPortableObjectAtLane(laneIndex);
            if (!hadItem && portableObject == null)
            {
                continue;
            }

            ClearConveyorItemAtLane(laneIndex, false);
            ReleaseFloorObject(portableObject);
            if (hadItem)
            {
                clearedCount++;
                activeTerrain?.NotifyConveyorLaneVacated(this, laneIndex);
            }
        }

        if (clearedCount > 0)
        {
            NotifyRuntimeItemStackChanged();
        }

        RefreshConveyorActivityRegistration(false, false);
        return clearedCount;
    }
}
