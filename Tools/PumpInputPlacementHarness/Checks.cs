using System;
using UnityEngine;
using T = InputOutputModule.RectGridBlockType;

public static class Checks
{
    private static int checks;
    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static InputOutputModule Area(Vector2Int coordinate, Vector2Int inward, T type) {
        var area = new InputOutputModule { Anchor = coordinate, InwardDirection = inward };
        area.RectGridPlacements.Add(new() { blockType = type });
        return area;
    }
    public static void Main()
    {
        for (int turns = 0; turns < 4; turns++)
        {
            var axis = InputOutputModule.Rotate(new(1, 0), turns);
            var pump = new Pump { Anchor = new(10, 20), Turns = turns };
            var firstBody = pump.Anchor;
            var secondBody = firstBody + axis;
            var firstPort = firstBody - axis;
            var secondPort = secondBody + axis;
            Require(pump.TryGetRuntimeFluidEndpoints(out var inlet, out var outlet)
                && inlet == firstPort && outlet == secondPort, "Rotated footprint must preserve inlet/outlet roles");
            Require(pump.AllowsRuntimeFluidTraversal(firstPort, false)
                && !pump.AllowsRuntimeFluidTraversal(firstPort, true), "Inlet permits delivery downstream only");
            Require(pump.AllowsRuntimeFluidTraversal(secondPort, true)
                && !pump.AllowsRuntimeFluidTraversal(secondPort, false), "Outlet permits source lookup upstream only");
            foreach (bool left in new[] { true, false })
            {
                var body = left ? firstBody : secondBody;
                var outward = left ? -axis : axis;
                var farPort = left ? secondPort : firstPort;
                InputOutputModule.ClearAreas();
                Require(pump.TryGetPipePassAt(pump, pump.Anchor, turns, body, out var remote, out var direction)
                    && remote == farPort && direction == outward, "Body inlet must reach the opposite pump end");
                Require(!pump.TryGetRuntimePipePass(body, out _, out _), "Empty body cell must not be a free junction");
                var controller = new InstallationPlacementController();
                foreach (var type in new[] { T.InputItem, T.InputEnergy, T.PipeInputItem, T.PipeInputEnergy, T.DoubleInputItem })
                {
                    var area = Area(body, outward, type);
                    Require(controller.Overlap(pump, body, area, type), "Pump body must fit input area");
                }
                var input = Area(body, outward, T.PipeInputItem);
                InputOutputModule.Register(body, input);
                Require(pump.TryGetRuntimePipePass(body, out remote, out direction)
                    && remote == farPort && direction == outward, "Registered direct fluid input must route upstream");
                input.InwardDirection = -outward;
                Require(!pump.TryGetRuntimePipePass(body, out _, out _), "Reversed input must not connect");
                Require(!controller.Overlap(pump, body, input, T.PipeInputItem), "Reversed input must not install");
                input.InwardDirection = outward;
                input.Fluid = 2;
                Require(!controller.Overlap(pump, body, input, T.PipeInputItem), "Wrong fluid must not install");
                Require(!controller.Overlap(pump, body, input, T.Object), "Body-to-body overlap must not install");
                Require(!controller.Overlap(pump, body, input, T.Output), "Output must not gain implicit overlap permission");
                InputOutputModule.ClearAreas();
                InputOutputModule.Register(body, Area(body, outward, T.PipeInput));
                Require(pump.TryGetRuntimePipePass(body, out _, out _), "Facing Pipe Pass must route through body inlet");
                if (!left)
                {
                    foreach (var type in new[] { T.PipeInput, T.PipeInputItem, T.PipeInputEnergy, T.DoubleInputItem })
                    {
                        InputOutputModule.ClearAreas();
                        InputOutputModule.Register(body, Area(body, outward, type));
                        Require(pump.TryGetRuntimePipePass(firstPort, out remote, out _)
                            && remote == body, "Forward Pump delivery must reach the machine's actual input cell");
                        Require(pump.ResolveRuntimeFluidDeliveryCoordinate(secondPort) == body,
                            "Vehicle unloading must use the same direct delivery cell");
                        Require(pump.AllowsRuntimeFluidTraversal(body, true),
                            "Direct machine input must permit upstream withdrawal");
                    }
                }
            }
            var flushPump = new Pump { Anchor = secondPort, Turns = turns };
            Require(flushPump.TryGetInterlockedEndpointAt(flushPump, flushPump.Anchor, turns,
                pump, pump, pump.Anchor, turns, secondPort, out var flushPort)
                && flushPort == secondBody, "Flush pump chain must retain reciprocal connection");
            var controller2 = new InstallationPlacementController {
                ExistingArea = Area(firstBody, -axis, T.PipeInputItem) };
            Require(controller2.Resolve(new(firstBody), pump, out var anchor, out _)
                && anchor.Coordinate == firstBody, "Clicking an input must place the body there, not shift to the marker");
            controller2.ExistingArea = Area(firstBody, -axis, T.PipeInput);
            Require(controller2.Resolve(new(firstBody), pump, out anchor, out _)
                && anchor.Coordinate == firstBody, "Clicking Pipe Pass must retain body-first placement");
        }
        Console.WriteLine($"Pump input placement checks passed: {checks}");
    }
}
