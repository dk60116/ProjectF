using System;
using UnityEngine;

public static class Checks
{
    private const int HeavyOilItemId = 113;
    private const int PetroleumGasItemId = 114;
    private static int checks;

    public static void Main()
    {
        EmptyAdjacentTanksRemainConnectable();
        SameOutputFluidRemainsConnectable();
        DifferentOutputFluidsStaySeparated();
        StoredFluidRejectsDifferentNetworkFluid();
        ConflictingTankSideNetworksStaySeparated();
        RejectedNeighborTankDoesNotPoisonValidBoundary();
        EmptyTankRejectsDifferentPipeNetwork();
        EmptyTankAcceptsSamePipeNetwork();
        UnclaimedEmptyTankAcceptsFirstPipeNetwork();
        MountedTankIsNotJoinedAsAFixedTank();
        PipeTraversalStopsAtRejectedTankBoundary();
        PipeTraversalContinuesAcrossCompatibleTankBoundary();
        PipeTraversalStopsAtRejectedPipeTankBoundary();
        PipeTraversalContinuesAcrossCompatiblePipeTankBoundary();
        FluidIdentitySearchAllowsEmptyTankToJoinKnownFluid();
        FluidIdentitySearchRejectsKnownFluidMismatch();
        Console.WriteLine($"Fluid tank network checks passed: {checks}");
    }

    private static void EmptyAdjacentTanksRemainConnectable()
    {
        Require(
            new Fluidtank().CanConnect(Vector2Int.right, new Fluidtank()),
            "two empty fixed tanks must still be allowed to form a storage bank");
    }

    private static void SameOutputFluidRemainsConnectable()
    {
        Fluidtank left = new Fluidtank();
        Fluidtank right = new Fluidtank();
        left.SetConnectedFluid(Vector2Int.left, HeavyOilItemId);
        right.SetConnectedFluid(Vector2Int.right, HeavyOilItemId);

        Require(
            left.CanConnect(Vector2Int.right, right),
            "adjacent tanks connected to the same output fluid must remain one bank");
    }

    private static void DifferentOutputFluidsStaySeparated()
    {
        Fluidtank left = new Fluidtank();
        Fluidtank right = new Fluidtank();
        left.SetConnectedFluid(Vector2Int.left, HeavyOilItemId);
        right.SetConnectedFluid(Vector2Int.right, PetroleumGasItemId);

        Require(
            !left.CanConnect(Vector2Int.right, right),
            "adjacent tanks connected to different output fluids must not merge networks");
    }

    private static void StoredFluidRejectsDifferentNetworkFluid()
    {
        Fluidtank left = new Fluidtank { StoredFluidItemId = HeavyOilItemId };
        Fluidtank right = new Fluidtank();
        right.SetConnectedFluid(Vector2Int.right, PetroleumGasItemId);

        Require(
            !left.CanConnect(Vector2Int.right, right),
            "a tank's stored fluid identity must participate in adjacent-bank compatibility");
    }

    private static void ConflictingTankSideNetworksStaySeparated()
    {
        Fluidtank left = new Fluidtank();
        Fluidtank right = new Fluidtank();
        left.SetConnectedFluid(Vector2Int.left, HeavyOilItemId);
        left.SetConnectedFluid(Vector2Int.up, PetroleumGasItemId);

        Require(
            !left.CanConnect(Vector2Int.right, right),
            "a tank already touching conflicting fluid networks must not propagate that conflict to an adjacent tank");
    }

    private static void RejectedNeighborTankDoesNotPoisonValidBoundary()
    {
        Fluidtank emptyTank = new Fluidtank();
        Fluidtank heavyOilTank = new Fluidtank();
        Fluidtank petroleumGasTank = new Fluidtank { StoredFluidItemId = PetroleumGasItemId };
        heavyOilTank.SetConnectedFluid(Vector2Int.down, HeavyOilItemId);
        heavyOilTank.SetAdjacentTank(Vector2Int.up, petroleumGasTank);

        Require(
            emptyTank.CanConnect(Vector2Int.right, heavyOilTank),
            "an incompatible adjacent tank must not prevent another empty tank from joining the valid local fluid network");
    }

    private static void EmptyTankRejectsDifferentPipeNetwork()
    {
        Fluidtank tank = new Fluidtank();
        tank.SetConnectedFluid(Vector2Int.left, HeavyOilItemId);

        Require(
            !tank.CanConnectPipe(Vector2Int.right, PetroleumGasItemId),
            "an empty tank claimed by one pipe network must reject a different pipe fluid");
    }

    private static void EmptyTankAcceptsSamePipeNetwork()
    {
        Fluidtank tank = new Fluidtank();
        tank.SetConnectedFluid(Vector2Int.left, HeavyOilItemId);

        Require(
            tank.CanConnectPipe(Vector2Int.right, HeavyOilItemId),
            "an empty tank must accept another pipe carrying the same established network fluid");
    }

    private static void UnclaimedEmptyTankAcceptsFirstPipeNetwork()
    {
        Require(
            new Fluidtank().CanConnectPipe(Vector2Int.right, HeavyOilItemId),
            "an empty tank without an established network fluid must accept its first pipe network");
    }

    private static void MountedTankIsNotJoinedAsAFixedTank()
    {
        Fluidtank mounted = new Fluidtank { IsFlatCarMounted = true };
        Require(
            !new Fluidtank().CanConnect(Vector2Int.right, mounted),
            "a mounted tank must continue through its docking rules instead of fixed-tank bank rules");
    }

    private static void PipeTraversalStopsAtRejectedTankBoundary()
    {
        Fluidtank current = new Fluidtank();
        current.SetNetworkConnectionAllowed(false);

        Require(
            !Pipe.CanTraverse(current, Vector2Int.right, new Fluidtank()),
            "pipe network search must not cross an incompatible adjacent-tank boundary");
    }

    private static void PipeTraversalContinuesAcrossCompatibleTankBoundary()
    {
        Require(
            Pipe.CanTraverse(new Fluidtank(), Vector2Int.right, new Fluidtank()),
            "pipe network search must continue across a compatible adjacent-tank boundary");
    }

    private static void PipeTraversalStopsAtRejectedPipeTankBoundary()
    {
        Fluidtank tank = new Fluidtank();
        tank.SetNetworkConnectionAllowed(false);

        Require(
            !Pipe.CanTraverseFromPipeToTank(Vector2Int.right, tank),
            "pipe network search must not enter a tank whose connected fluid is incompatible");
    }

    private static void PipeTraversalContinuesAcrossCompatiblePipeTankBoundary()
    {
        Require(
            Pipe.CanTraverseFromPipeToTank(Vector2Int.right, new Fluidtank()),
            "pipe network search must enter a tank whose connected fluid is compatible");
    }

    private static void FluidIdentitySearchAllowsEmptyTankToJoinKnownFluid()
    {
        Require(
            Pipe.CanTraverseDuringFluidIdentitySearch(
                new Fluidtank(),
                Vector2Int.right,
                new Fluidtank { StoredFluidItemId = HeavyOilItemId }),
            "an empty tank must be able to join a fixed tank with an established fluid identity");
    }

    private static void FluidIdentitySearchRejectsKnownFluidMismatch()
    {
        Require(
            !Pipe.CanTraverseDuringFluidIdentitySearch(
                new Fluidtank { StoredFluidItemId = HeavyOilItemId },
                Vector2Int.right,
                new Fluidtank { StoredFluidItemId = PetroleumGasItemId }),
            "a nested identity lookup must reject two known, different stored fluids without recursive searching");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        checks++;
    }
}
