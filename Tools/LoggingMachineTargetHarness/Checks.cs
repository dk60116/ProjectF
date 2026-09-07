using System;
using System.Collections.Generic;
using UnityEngine;

public static class Checks
{
    private static int passed;

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new Exception(label);
        }

        passed++;
        Console.WriteLine("PASS " + label);
    }

    public static void Main()
    {
        TerrainGenerator.Active = new TerrainGenerator();
        var machine = new LoggingMachine
        {
            RuntimeAnchorCoordinate = new Vector2Int(10, 10),
            RuntimeQuarterTurns = 0,
            MinimumGrowth = 10
        };

        var block = new Block();
        var tree = new ProjectF.MapObjects.Tree
        {
            OwningBlock = block,
            Growth = 10f
        };
        block.Resource = tree;
        TerrainGenerator.Active.Blocks[new Vector2Int(10, 9)] = block;

        Check(machine.RecognizesDirection(0),
            "tree recognition is independent of log output capacity");
        Check(machine.HasRecognizedTree(),
            "blocked tree remains present for status and targeting");

        tree.Growth = 9f;
        Check(!machine.RecognizesDirection(0),
            "tree below the configured growth threshold is not harvestable");
        tree.Growth = 10f;
        tree.FilterEnabled = false;
        Check(!machine.RecognizesDirection(0),
            "disabled tree filter remains respected");
        tree.FilterEnabled = true;
        tree.ResolvedHarvestMode = Resource.HarvestMode.Mining;
        Check(!machine.RecognizesDirection(0),
            "non-logging resources remain excluded");
        tree.ResolvedHarvestMode = Resource.HarvestMode.Logging;
        tree.gameObject.activeInHierarchy = false;
        Check(!machine.RecognizesDirection(0),
            "inactive depleted resource is not recognized as a live tree");

        Check(LoggingMachine.GetHarvestCoordinate(new Vector2Int(3, 4), 1, 0)
              == new Vector2Int(4, 4),
            "harvest direction rotates with installation orientation");

        Console.WriteLine($"{passed} logging target checks passed.");
    }
}

public class ResourceDefinition
{
    public const int MaxGrowth = 10;
}

public class Resource
{
    public enum HarvestMode
    {
        Auto,
        Mining,
        Logging,
        Cut,
        Cultivating
    }

    public HarvestMode ResolvedHarvestMode = HarvestMode.Logging;
    public bool CanHarvest = true;
    public GameObject gameObject = new GameObject();
    public ResourceDefinition Definition = new ResourceDefinition();
    public Block OwningBlock;
    public bool FilterEnabled = true;

    public bool TryPeekMachineHarvestOutput(out int itemId, out int count)
    {
        itemId = 1;
        count = 4;
        return CanHarvest;
    }
}

namespace ProjectF.MapObjects
{
    public class Tree : Resource
    {
        public float Growth;
    }
}

public class Block
{
    public Resource Resource;
}

public class TerrainGenerator
{
    public static TerrainGenerator Active;
    public readonly Dictionary<Vector2Int, Block> Blocks = new Dictionary<Vector2Int, Block>();

    public static TerrainGenerator ResolveActive() => Active;

    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block)
    {
        return Blocks.TryGetValue(coordinate, out block);
    }
}

public static class InputOutputModule
{
    public static Vector2Int RotateRectGridOffset(Vector2Int offset, int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            offset = new Vector2Int(-offset.y, offset.x);
        }

        return offset;
    }
}

public partial class LoggingMachine
{
    private static readonly Vector2Int[] LocalHarvestDirections =
    {
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.right
    };

    public Vector2Int RuntimeAnchorCoordinate;
    public int RuntimeQuarterTurns;
    public int MinimumGrowth;

    public bool RecognizesDirection(int directionIndex)
    {
        return TryResolveAdjacentTree(directionIndex, out _);
    }

    public bool HasRecognizedTree() => HasAnyAdjacentTree();

    private bool IsTreeTypeEnabled(ResourceDefinition definition)
    {
        foreach (Block block in TerrainGenerator.Active.Blocks.Values)
        {
            if (block.Resource != null && block.Resource.Definition == definition)
            {
                return block.Resource.FilterEnabled;
            }
        }

        return false;
    }
}

namespace UnityEngine
{
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
    }

    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x;
        public readonly int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int down => new Vector2Int(0, -1);
        public static Vector2Int left => new Vector2Int(-1, 0);
        public static Vector2Int up => new Vector2Int(0, 1);
        public static Vector2Int right => new Vector2Int(1, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) =>
            new Vector2Int(a.x + b.x, a.y + b.y);
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
    }
}
