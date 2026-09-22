using System;
using UnityEngine;

public static class Checks
{
    private static int checks;

    public static void Main()
    {
        NewPipeCannotOverlapExistingPipe();
        IgnoredPreviewDoesNotBlockItself();
        NonPipePlacementIsUnaffected();
        EmptyCoordinateIsUnaffected();
        StraightExtensionBeatsForeignBranches();
        ParallelLinesKeepTheirFluids();
        ExistingIdentityAndEndpointsRemainStable();
        ManualAndUndergroundPortsAreRespected();
        UndergroundFluidTravelsThroughInstalledPair();
        PumpEndpointsUsePipeCompatibility();
        ProposedPumpUsesBothEndpointFluids();
        PumpBodyInputUsesNetworkFluid();
        Console.WriteLine($"Pipe placement checks passed: {checks}");
    }

    private static void NewPipeCannotOverlapExistingPipe()
    {
        Require(
            Invoke(new Pipe(), new Pipe(), null),
            "a new pipe must be rejected when another pipe already occupies the coordinate");
    }

    private static void IgnoredPreviewDoesNotBlockItself()
    {
        Pipe preview = new Pipe();
        Require(
            !Invoke(new Pipe(), preview, preview),
            "the preview explicitly ignored by placement validation must not block itself");
    }

    private static void NonPipePlacementIsUnaffected()
    {
        Require(
            !Invoke(new MapObject(), new Pipe(), null),
            "the duplicate-pipe guard must not reject non-pipe placements");
    }

    private static void EmptyCoordinateIsUnaffected()
    {
        Require(
            !Invoke(new Pipe(), null, null),
            "a pipe placement must remain valid when no existing pipe is present");
    }

    private static bool Invoke(MapObject candidate, Pipe existing, MapObject ignored)
    {
        var method = typeof(InstallationPlacementController).GetMethod(
            "IsDuplicatePipePlacement",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method != null && (bool)method.Invoke(null, new object[] { candidate, existing, ignored });
    }

    private const int OrangeFluid = 112;
    private const int GreenFluid = 113;

    private static Pipe Neighbor(int fluid, PipeVariantKind kind, int mask,
        bool preview = false, long sequence = 1) => new Pipe
    {
        Fluid = fluid, VariantKind = kind, Mask = mask, Preview = preview, Sequence = sequence
    };

    private static int RotateMask(int mask, int turns) =>
        ((mask << turns) | (mask >> (4 - turns))) & 15;

    private static void StraightExtensionBeatsForeignBranches()
    {
        for (int turns = 0; turns < 4; turns++)
        {
            foreach (bool preview in new[] { false, true })
            {
                var controller = new InstallationPlacementController();
                controller.SetNeighbor((3 + turns) & 3,
                    Neighbor(OrangeFluid, PipeVariantKind.Straight, RotateMask(10, turns)));
                // Three newer green tee/corner pieces surround the end of an orange line.
                controller.SetNeighbor(turns,
                    Neighbor(GreenFluid, PipeVariantKind.Tee, RotateMask(14, turns), preview, 10));
                controller.SetNeighbor((1 + turns) & 3,
                    Neighbor(GreenFluid, PipeVariantKind.Corner, RotateMask(9, turns), preview, 11));
                controller.SetNeighbor((2 + turns) & 3,
                    Neighbor(GreenFluid, PipeVariantKind.Corner, RotateMask(3, turns), preview, 12));
                Require(controller.Select(preview: preview) == OrangeFluid,
                    $"straight orange end must beat three green branches, rotation {turns}, preview {preview}");
                Require(controller.Select(GreenFluid, preview) == OrangeFluid,
                    "committed normalization must use the same straight-continuation priority");
            }
        }
    }

    private static void ParallelLinesKeepTheirFluids()
    {
        for (int turns = 0; turns < 4; turns++)
        {
            foreach (int fluid in new[] { OrangeFluid, GreenFluid })
            {
                int otherFluid = fluid == OrangeFluid ? GreenFluid : OrangeFluid;
                var controller = new InstallationPlacementController();
                controller.SetNeighbor((3 + turns) & 3,
                    Neighbor(fluid, PipeVariantKind.Straight, RotateMask(10, turns)));
                controller.SetNeighbor(turns,
                    Neighbor(otherFluid, PipeVariantKind.Straight, RotateMask(10, turns), true, 20));
                Require(controller.Select(preview: true) == fluid,
                    "parallel straight lines must extend their own fluid instead of creating a side branch");
            }
        }
    }

    private static void ExistingIdentityAndEndpointsRemainStable()
    {
        var controller = new InstallationPlacementController();
        Require(controller.Select() == -1, "no typed neighbors must leave a new pipe untyped");
        Require(controller.Select(GreenFluid) == GreenFluid, "isolated pipes must keep their fluid");
        controller.SetNeighbor(3, Neighbor(OrangeFluid, PipeVariantKind.Straight, 10));
        controller.SetNeighbor(1, Neighbor(GreenFluid, PipeVariantKind.Straight, 10, sequence: 20));
        Require(controller.Select(OrangeFluid) == OrangeFluid,
            "equally aligned installed neighbors must not steal the anchor's established fluid");
        controller.EndpointFluid = GreenFluid;
        Require(controller.Select(OrangeFluid, true) == GreenFluid,
            "a machine's own PipeOutput fluid must override all neighboring priorities");

        controller = new InstallationPlacementController();
        controller.SetNeighbor(3, Neighbor(OrangeFluid, PipeVariantKind.Corner, 3));
        Require(controller.Select() == OrangeFluid, "a single corner remains a valid extension");
        controller.SetNeighbor(1, Neighbor(GreenFluid, PipeVariantKind.Corner, 9, true, 20));
        Require(controller.Select(preview: true) == GreenFluid,
            "equally ranked candidates must still prefer the active blueprint");
        controller.SetNeighbor(0, Neighbor(OrangeFluid, PipeVariantKind.Corner, 6));
        Require(controller.Select() == OrangeFluid,
            "compatible neighbor count must still break equal-geometry ties");
    }

    private static void ManualAndUndergroundPortsAreRespected()
    {
        var controller = new InstallationPlacementController();
        Pipe blockedOrange = Neighbor(OrangeFluid, PipeVariantKind.Straight, 10);
        blockedOrange.ManualMask = 5;
        controller.SetNeighbor(3, blockedOrange);
        controller.SetNeighbor(0, Neighbor(GreenFluid, PipeVariantKind.Corner, 6));
        Require(controller.Select() == GreenFluid,
            "an explicitly closed orange port must not attract a connection");
        controller.SetNeighbor(3, new UndergroundPipe
            { Fluid = OrangeFluid, Pair = new[] { Vector2Int.left, new Vector2Int(3, 0) } });
        Require(controller.Select() == GreenFluid,
            "the back of an underground endpoint must not attract a connection");
        controller.SetNeighbor(3, new UndergroundPipe
            { Fluid = OrangeFluid, Pair = new[] { Vector2Int.left, new Vector2Int(-5, 0) } });
        controller.SetNeighbor(0, Neighbor(GreenFluid, PipeVariantKind.Straight, 10));
        Require(controller.Select() == OrangeFluid,
            "an exposed underground port must still beat a closed side branch");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        checks++;
    }

    private static void UndergroundFluidTravelsThroughInstalledPair()
    {
        for (int mode = 0; mode < 3; mode++) // Runtime record, saved state, scene preview.
        for (int turns = 0; turns < 4; turns++)
        for (int reversed = 0; reversed < 2; reversed++)
        foreach (int fluid in new[] { OrangeFluid, GreenFluid })
        {
            PipeWorld.Current = new PipeWorld();
            int otherFluid = fluid == OrangeFluid ? GreenFluid : OrangeFluid;
            var controller = new InstallationPlacementController();
            var tunnel = new UndergroundPipe(); // Prefab has no installed endpoints or cached fluid.
            var near = RotateCoordinate(new Vector2Int(-1, 0), turns);
            var far = RotateCoordinate(new Vector2Int(-5, 0), turns);
            var first = reversed == 0 ? near : far;
            var second = reversed == 0 ? far : near;
            if (mode == 2)
            {
                tunnel.Pair = new[] { first, second };
                tunnel.gameObject.scene.Valid = true;
                controller.SetPipe(first, tunnel);
                controller.SetPipe(second, tunnel);
            }
            else
            {
                controller.AddUndergroundPair(tunnel, first, second, mode == 1);
            }
            controller.SetPipe(RotateCoordinate(new Vector2Int(-6, 0), turns),
                Neighbor(fluid, PipeVariantKind.Straight, RotateMask(10, turns)));
            controller.SetNeighbor(turns,
                Neighbor(otherFluid, PipeVariantKind.Straight, RotateMask(10, turns), true, 20));
            Require(controller.Collect(near, tunnel, out var fluids) && fluids.SetEquals(new[] { fluid }),
                $"remote source identity must traverse tunnel: mode {mode}, rotation {turns}, reversed {reversed}");
            Require(controller.Select(preview: true) == fluid && controller.Select() == fluid,
                "both blueprint and installed resolution must extend the underground source's fluid");
            Require(controller.HasPort(near, tunnel, -near) && !controller.HasPort(near, tunnel, near),
                "explicit rotation must recognize the outward endpoint and reject its back");
            Require(controller.CanPlace(Neighbor(-1, PipeVariantKind.Straight, RotateMask(10, turns))),
                "a same-axis extension from the tunnel must remain a valid placement");
            Require(!controller.CanPlace(Neighbor(-1, PipeVariantKind.Corner, RotateMask(9, turns))),
                "a corner from the tunnel into the different-fluid neighboring row must be rejected");
        }
        PipeWorld.Current = null;
    }

    private static Vector2Int RotateCoordinate(Vector2Int coordinate, int turns)
    {
        for (int i = 0; i < turns; i++) coordinate = new Vector2Int(coordinate.y, -coordinate.x);
        return coordinate;
    }

    private static void ProposedPumpUsesBothEndpointFluids()
    {
        for (int turns = 0; turns < 4; turns++)
        foreach (bool overlap in new[] { false, true })
        foreach (bool pipes in new[] { false, true })
        foreach (int fluid in new[] { -1, GreenFluid, OrangeFluid })
        {
            PipeWorld.Current = null;
            var controller = new InstallationPlacementController();
            Vector2Int direction = RotateCoordinate(new(1, 0), turns);
            var pump = new Pump { First = default, FirstOut = -direction,
                Second = direction + direction, SecondOut = direction };
            Vector2Int first = overlap ? pump.First : pump.First - direction;
            Vector2Int second = overlap ? pump.Second : pump.Second + direction;
            if (pipes)
            {
                controller.SetPipe(first, Neighbor(GreenFluid, PipeVariantKind.Straight, RotateMask(10, turns)));
                controller.SetPipe(second, Neighbor(fluid, PipeVariantKind.Straight, RotateMask(10, turns)));
            }
            else
            {
                controller.SetTank(first, GreenFluid);
                controller.SetTank(second, fluid);
            }
            Require(controller.CanPlacePump(pump) == (fluid < 0 || fluid == GreenFluid),
                "new Pump must compare both tank/pipe endpoint fluids, including overlap");
        }
    }

    private static void PumpBodyInputUsesNetworkFluid()
    {
        for (int turns = 0; turns < 4; turns++)
        foreach (int fluid in new[] { GreenFluid, OrangeFluid })
        {
            PipeWorld.Current = null;
            var controller = new InstallationPlacementController();
            Vector2Int direction = RotateCoordinate(new(1, 0), turns);
            var pump = new Pump { First = -direction, FirstOut = -direction,
                Second = direction + direction, SecondOut = direction };
            controller.SetTank(pump.First, GreenFluid);
            controller.SetInputArea(direction, fluid);
            Require(controller.CanPlacePump(pump) == (fluid == GreenFluid),
                "input under Pump body must match fluid from the opposite endpoint");
        }
    }

    private static void PumpEndpointsUsePipeCompatibility()
    {
        for (int turns = 0; turns < 4; turns++)
        foreach (bool overlappingTank in new[] { false, true })
        foreach (int tankFluid in new[] { -1, GreenFluid, OrangeFluid })
        {
            PipeWorld.Current = null;
            var controller = new InstallationPlacementController();
            Vector2Int near = RotateCoordinate(new(-1, 0), turns);
            Vector2Int far = RotateCoordinate(new(-4, 0), turns);
            controller.AddPump(near, -near, far, near);
            controller.SetTank(overlappingTank ? far : far + near, tankFluid);
            controller.SetNeighbor(turns,
                Neighbor(OrangeFluid, PipeVariantKind.Straight, RotateMask(10, turns), true, 20));
            Pipe extension = Neighbor(GreenFluid, PipeVariantKind.Straight, RotateMask(10, turns));
            Require(controller.CanPlace(extension) == (tankFluid < 0 || tankFluid == GreenFluid),
                "Pump must allow empty/same-fluid tanks and reject foreign tanks, including endpoint overlap");
            if (tankFluid == GreenFluid)
            {
                Require(controller.Select(preview: true) == GreenFluid,
                    "a Pump with remote green tank must take priority over the orange side branch");
            }
        }
    }
}
