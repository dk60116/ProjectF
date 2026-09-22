using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => default;
        public static Vector2Int right => new(1, 0);
        public static Vector2Int left => new(-1, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a) => new(-a.x, -a.y);
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
    }
}

public sealed class PipeRuntimeRecord
{
    public int FluidItemId;
    public float Pressure;
    public UnityEngine.Vector2Int Facing;

    public bool HasConnectionTowardsAt(UnityEngine.Vector2Int coordinate, UnityEngine.Vector2Int direction) =>
        direction == Facing;

    public bool TryGetObjectInfoFluidInfo(
        UnityEngine.Vector2Int coordinate,
        out int fluidItemId,
        out float temperature,
        out float pressure)
    {
        fluidItemId = FluidItemId;
        temperature = 20f;
        pressure = Pressure;
        return fluidItemId >= 0;
    }
}

public sealed class PipeWorld
{
    public static PipeWorld Current { get; set; } = new();
    public readonly Dictionary<UnityEngine.Vector2Int, PipeRuntimeRecord> Records = new();
    public bool TryGetAtCoordinate(UnityEngine.Vector2Int coordinate, out PipeRuntimeRecord record) =>
        Records.TryGetValue(coordinate, out record);
}

public class Pipe
{
    public class FluidNetworkSearchContext {}
    public static bool TryGetNetworkFluidInfoAt(UnityEngine.Vector2Int coordinate, FluidNetworkSearchContext context,
        bool ignored, UnityEngine.Vector2Int ignoredCoordinate, bool includePressure,
        out int fluid, out float temperature, out float pressure)
    {
        if (PipeWorld.Current.TryGetAtCoordinate(coordinate, out var record))
            return record.TryGetObjectInfoFluidInfo(coordinate, out fluid, out temperature, out pressure);
        fluid = -1; temperature = pressure = 0; return false;
    }
}
public class Pump { public bool AllowsRuntimeFluidTraversal(UnityEngine.Vector2Int coordinate, bool upstream) => false; }
public partial class InputOutputModule
{
    private List<RuntimePumpPipePass> connectedFluidPumpPassScratch;
    internal readonly record struct RuntimePumpPipePass(Pump Pump, UnityEngine.Vector2Int ExternalDirection);
    private static bool CollectPumpPipePassesAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, List<RuntimePumpPipePass> results) => false;
    private static bool TryGetPassiveFluidPassAtRuntimeCoordinate(UnityEngine.Vector2Int coordinate, out object owner,
        out UnityEngine.Vector2Int other, out UnityEngine.Vector2Int external)
    { owner = null; other = external = default; return false; }
    public UnityEngine.Vector2Int ExternalDirection = UnityEngine.Vector2Int.right;

    protected bool TryGetRuntimePipeAreaExternalDirection(
        UnityEngine.Vector2Int coordinate, out UnityEngine.Vector2Int direction)
    {
        direction = ExternalDirection;
        return direction != UnityEngine.Vector2Int.zero;
    }

    public bool ReadPressure(UnityEngine.Vector2Int coordinate, int fluidItemId, out float pressure) =>
        TryGetRuntimeFluidInputPressure(coordinate, fluidItemId, out pressure);
}

public static class Checks
{
    private static readonly UnityEngine.Vector2Int Input = new(5, 8);
    private static readonly UnityEngine.Vector2Int Outside = new(6, 8);
    private const int Water = 42;
    private const int Oil = 43;
    private static int checks;

    public static void Main()
    {
        var machine = new InputOutputModule();
        PipeWorld.Current.Records[Input] = new PipeRuntimeRecord
        {
            FluidItemId = Water, Pressure = 3.5f, Facing = UnityEngine.Vector2Int.left
        };
        Require(machine.ReadPressure(Input, Water, out float pressure) && pressure == 3.5f,
            "a pipe on DoubleInput must supply its actual pressure");

        PipeWorld.Current.Records[Outside] = new PipeRuntimeRecord
        {
            FluidItemId = Water, Pressure = 9f, Facing = UnityEngine.Vector2Int.left
        };
        Require(machine.ReadPressure(Input, Water, out pressure) && pressure == 3.5f,
            "the pipe on DoubleInput must take precedence over the adjacent pipe");

        PipeWorld.Current.Records.Remove(Input);
        Require(machine.ReadPressure(Input, Water, out pressure) && pressure == 9f,
            "an adjacent pipe must remain a valid input when DoubleInput has no pipe");

        PipeWorld.Current.Records[Input] = new PipeRuntimeRecord
        {
            FluidItemId = Water, Pressure = 4f, Facing = UnityEngine.Vector2Int.right
        };
        Require(!machine.ReadPressure(Input, Water, out pressure) && pressure == 0f,
            "a pipe facing away from the machine must not supply pressure");

        PipeWorld.Current.Records[Input].Facing = UnityEngine.Vector2Int.left;
        Require(!machine.ReadPressure(Input, Oil, out pressure) && pressure == 0f,
            "another fluid must not satisfy the recipe input");
        Console.WriteLine($"Production fluid input checks passed: {checks}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
