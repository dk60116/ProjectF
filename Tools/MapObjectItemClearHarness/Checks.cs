using System;
using System.Collections.Generic;

static class Checks
{
    private static int passed;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }

    private static void Main()
    {
        InputOutputModule.Definitions[10] = new ItemDefinition { mapObject = new Desk() };
        InputOutputModule.Definitions[20] = new ItemDefinition { mapObject = new InstallationObject() };

        var deskState = new BlockStateStore.InstallationSaveState
        {
            itemId = 10,
            storedInstallationItemId = 500,
            storedInstallationItemIds = new List<int> { 1, -1 },
            robotArmState = new RobotArm.TransferState { heldItemId = 2 },
            inputOutputState = new InputOutputModule.PersistentState
            {
                activeOutputItemId = 3,
                activeOutputCount = 2,
                storedEnergyUnits = 5,
                hasActiveCraft = true
            },
            hasSteamTrainBurnEnergyState = true,
            steamTrainStoredBurnEnergyUnits = 7,
            hasDeterministicUnits = true
        };

        BlockStateStore.MapObjectItemClearResult deskResult =
            BlockStateStore.ProbeClear(deskState, true);
        Check(deskState.storedInstallationItemId == 500,
            "saved Desk manual must survive MapObject Item Clear");
        Check(deskResult.StoredItems == 1,
            "preserved Desk manual must not be counted as cleared");
        Check(deskState.storedInstallationItemIds.Count == 0
              && deskState.robotArmState == null
              && deskState.inputOutputState.Cleared
              && deskState.steamTrainStoredBurnEnergyUnits == 0,
            "other saved MapObject item state still clears");

        var normalState = new BlockStateStore.InstallationSaveState
        {
            itemId = 20,
            storedInstallationItemId = 501
        };
        BlockStateStore.MapObjectItemClearResult normalResult =
            BlockStateStore.ProbeClear(normalState, true);
        Check(normalState.storedInstallationItemId == -1 && normalResult.StoredItems == 1,
            "ordinary stored installation item still clears");
        Check(BlockStateStore.ProbePreserveLive(new Desk())
              && !BlockStateStore.ProbePreserveLive(new InstallationObject()),
            "live preservation is limited to Desk");

        Console.WriteLine($"PASS: {passed} MapObject item clear preservation checks. No engine launched.");
    }
}

public class InstallationObject { }
public sealed class Desk : InstallationObject { }
public sealed class ItemDefinition { public InstallationObject mapObject; }
public sealed class RobotArm
{
    public sealed class TransferState { public int heldItemId = -1; }
}
public static class DeterministicSimulationUnits
{
    public static long FromFloat(float value) => (long)Math.Max(0f, value);
}
public class InputOutputModule
{
    public static readonly Dictionary<int, ItemDefinition> Definitions = new();
    public static ItemDefinition ResolveItemDefinition(int itemId) =>
        Definitions.TryGetValue(itemId, out ItemDefinition definition) ? definition : null;

    public sealed class PersistentState
    {
        public int activeOutputCount;
        public int activeOutputItemId = -1;
        public bool hasDeterministicUnits;
        public long storedEnergyUnits;
        public float storedEnergy;
        public bool hasActiveCraft;
        public bool waitingForOutput;
        public long remainingCraftTicks;
        public float remainingCraftTime;
        public long activeCraftConsumedEnergyUnits;
        public float activeCraftConsumedEnergy;
        public long oilDrillingProgressUnits;
        public float oilDrillingProgressLiters;
        public long seedPlanterPlantElapsedUnits;
        public float seedPlanterPlantElapsedSeconds;
        public bool steamGeneratorHasGenerationReserve;
        public bool Cleared;
        public void ClearStoredEnergyAndProduction()
        {
            activeOutputCount = 0;
            activeOutputItemId = -1;
            storedEnergyUnits = 0;
            hasActiveCraft = false;
            Cleared = true;
        }
    }
}
public partial class BlockStateStore
{
    public sealed class InstallationSaveState
    {
        public int itemId = -1;
        public int storedInstallationItemId = -1;
        public List<int> storedInstallationItemIds;
        public RobotArm.TransferState robotArmState;
        public InputOutputModule.PersistentState inputOutputState;
        public bool hasDeterministicUnits;
        public bool hasSteamTrainBurnEnergyState;
        public float steamTrainStoredBurnEnergy;
        public float steamTrainBurnEnergyGaugeCapacity;
        public long steamTrainStoredBurnEnergyUnits;
        public long steamTrainBurnEnergyGaugeCapacityUnits;
    }
}
