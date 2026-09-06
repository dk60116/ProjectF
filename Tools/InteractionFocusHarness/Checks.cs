using System;
using System.Collections.Generic;

public static class Mathf
{
    public static float Abs(float v) => Math.Abs(v);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
}
public struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 zero => default;
    public static Vector3 one => new(1, 1, 1);
    public float sqrMagnitude => x * x + y * y + z * z;
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
}
public readonly record struct Vector2Int(int x, int y);
public struct Bounds
{
    private Vector3 center, size;
    public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    public void Expand(Vector3 expansion) { size.x += expansion.x; size.y += expansion.y; size.z += expansion.z; }
    public Vector3 ClosestPoint(Vector3 p) => new(
        Math.Clamp(p.x, center.x - size.x / 2, center.x + size.x / 2),
        Math.Clamp(p.y, center.y - size.y / 2, center.y + size.y / 2),
        Math.Clamp(p.z, center.z - size.z / 2, center.z + size.z / 2));
}
public class Transform { public Vector3 position; }
public class GameObject { public bool activeInHierarchy = true; }
public class MapObject
{
    public struct MapObjectStatus { public float mapSizeX, mapSizeY; }
    public MapObjectStatus Status = new() { mapSizeX = 1, mapSizeY = 1 };
    public int Id;
    public bool AllowsFocus = true;
    public float Distance;
    public GameObject gameObject = new();
    public Transform transform = new();
    public List<Block> Cells = new();
    public int GetInstanceID() => Id;
}
public class InstallationObject : MapObject { public List<Vector2Int> RuntimeOccupiedCoordinates = new(); }
public class InputOutputModule : InstallationObject { }
public class Resource : MapObject { public Block Block; }
public class Sprinkler { public static void CollectActiveSprinklersContainingWorldPosition(Vector3 origin, HashSet<Sprinkler> targets) { } }
public class Block
{
    public int Id;
    public MapObject Target;
    public Vector3 WorldPosition;
    public int GetInstanceID() => Id;
}
public class Player { public Transform BodyTransform = new(); public bool IsHoldingEmptyBucket; }
public partial class PlayerController
{
    private MapObject closestInteractionFocusTarget;
    private Block closestInteractionFocusBlock;
    private readonly Player player = new();
    private readonly Transform transform = new();
    private Block standaloneInteractionAreaFocusBlock;
    private readonly Dictionary<Block, MapObject> interactionFocusTargetOverrides = new();
    private readonly List<Block> combinedInteractionFocusBlocks = new(), nearbyInputOutputModuleFocusBlocks = new(), nearbyWorkableFocusBlocks = new(), nearbyBoxFocusBlocks = new(), nearbyInstallationFocusBlocks = new();
    private readonly List<MapObject> interactionButtonFocusTargets = new();
    private readonly List<Block> interactionButtonFocusTargetBlocks = new();
    private readonly HashSet<Block> currentInteractionFarmlandFocusGroup = new();
    private readonly HashSet<Sprinkler> nextInRangeSprinklerRangeObjects = new();
    private readonly List<MapObject> nearbyWorkableRangeObjects = new();
    private object MountedVehicle => null;
    public List<Block> Candidates = new(), Focused = new();
    public Block Watering, StandingArea;
    public InputOutputModule StandingOwner;
    public int ButtonCount => interactionButtonFocusTargets.Count;
    public MapObject Selected => closestInteractionFocusTarget;
    public void Refresh() => RefreshInteractionFocus();
    public void Select(List<Block> blocks) => KeepClosestInteractionFocusTarget(blocks);
    public float Distance(InstallationObject target, Block block, Vector3 origin) => GetMapObjectFocusSelectionDistanceSqr(target, block, origin);
    private MapObject ResolveInteractionFocusTarget(Block block) => block != null && interactionFocusTargetOverrides.TryGetValue(block, out var target) ? target : block?.Target;
    private float GetInteractionFocusTargetDistanceSqr(MapObject target, Block block, Vector3 origin) => target != null ? target.Distance : block.WorldPosition.sqrMagnitude;
    private bool AppendMapObjectFocusBlocks(MapObject target, Block fallback, List<Block> result)
    {
        if (target.Cells.Count > 0) result.AddRange(target.Cells); else result.Add(fallback);
        return true;
    }
    private static void AppendUniqueBlock(List<Block> target, Block block) { if (block != null && !target.Contains(block)) target.Add(block); }
    private static void AppendUniqueBlocks(List<Block> target, List<Block> blocks) { foreach (var b in blocks) AppendUniqueBlock(target, b); }
    private void UpdateInRangeSprinklerRangeVisuals(object value) { }
    private void RefreshMountedPinnedInteractionFocus() { }
    private void ExpireTemporaryDropFocusIfNeeded() { }
    private bool TryGetStandingConveyorFocusBlock(out Block block) { block = null; return false; }
    private Resource FindNearestResourceInteractionTarget() => null;
    private Block ResolveResourceOwningBlock(Resource resource) => resource.Block;
    private bool TryGetStandingInputOutputAreaFocusBlock(out Block block, out InputOutputModule owner) { block = StandingArea; owner = StandingOwner; return block != null; }
    private bool FindCurrentInputOutputModuleFocusBlocks(List<Block> blocks) { blocks.Clear(); return false; }
    private void FindNearbyWorkableBlocks(List<Block> blocks) => blocks.Clear();
    private void UpdateSelectedWorkableRangeVisuals(object value) { }
    private void FindNearbyBoxBlocks(List<Block> blocks) => blocks.Clear();
    private void FindNearbyInstallationBlocks(List<Block> blocks, Block standing) { blocks.Clear(); blocks.AddRange(Candidates); }
    private void SetInteractionFocusTargetOverride(Block block, MapObject target) => interactionFocusTargetOverrides[block] = target;
    private bool TryGetStandingSeedGroundBlock(out Block block, out int id) { block = null; id = 0; return false; }
    private bool TryFindNearestBucketFluidSource(out Block block, out int id) { block = null; id = 0; return false; }
    private bool TryFindNearestPlantWateringTarget(out Block block, out int id) { block = Watering; id = 0; return block != null; }
    private void BuildStandingAreaAndObjectFocusBlocks(Block area, InputOutputModule owner, List<Block> blocks)
    {
        blocks.Clear(); if (owner != null) blocks.AddRange(owner.Cells); AppendUniqueBlock(blocks, area);
    }
    private bool TryFindNearestFarmlandFocusBlock(out Block block) { block = null; return false; }
    private bool AppendConnectedFarmlandFocusBlocks(Block block, List<Block> blocks, HashSet<Block> group) => false;
    private void SetFocusedBlocks(List<Block> blocks, bool group) { Focused.Clear(); Focused.AddRange(blocks); }
}
public static class Checks
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); passed++; }
    private static Block Candidate(int id, float distance) => new() { Id = id, Target = new MapObject { Id = id, Distance = distance } };
    public static void Main()
    {
        var controller = new PlayerController();
        var near = Candidate(20, 1); var far = Candidate(10, 4);
        var blocks = new List<Block> { far, near }; controller.Select(blocks);
        Check(blocks.Count == 1 && blocks[0] == near, "closest wins regardless of candidate order");
        far.Target.Distance = 1;
        for (int i = 0; i < 100; i++) { blocks = i % 2 == 0 ? new() { far, near } : new() { near, far }; controller.Select(blocks); }
        Check(blocks[0] == near, "equal-distance candidates keep the previous target across reordered frames");
        far.Target.Distance = .5f; blocks = new() { near, far }; controller.Select(blocks);
        Check(blocks[0] == far, "a closer candidate switches immediately");
        near.Target.Distance = .5f;
        var fresh = new PlayerController(); blocks = new() { near, far }; fresh.Select(blocks);
        Check(blocks[0] == far, "initial tie uses stable object identity");
        far.Target.gameObject.activeInHierarchy = false; blocks = new() { far, near }; controller.Select(blocks);
        Check(blocks[0] == near, "inactive previous target cannot retain focus");
        far.Target.gameObject.activeInHierarchy = true;
        near.Target.Distance = float.NaN; blocks = new() { near, far }; controller.Select(blocks);
        Check(blocks.Count == 1 && blocks[0] == far, "invalid distance cannot capture focus");

        near.Target.Distance = .25f; far.Target.Distance = 4;
        controller.Candidates = new() { far }; controller.Watering = near; controller.Refresh();
        Check(controller.Focused.Count == 1 && controller.Selected == near.Target, "late watering candidate participates in the final single selection");
        Check(controller.ButtonCount == 1, "interaction button candidates match the single visual focus");
        controller.Watering = far; controller.Candidates = new() { near }; controller.Refresh();
        Check(controller.Focused.Count == 1 && controller.Selected == near.Target, "farther late candidate cannot add a second focus");
        var owner = new InputOutputModule { Id = 30, Distance = 8 };
        var body = new Block { Id = 30, Target = owner }; owner.Cells.Add(body);
        controller.StandingArea = new Block { Id = 31 }; controller.StandingOwner = owner; controller.Refresh();
        Check(controller.Selected == near.Target && controller.Focused.Count == 1, "standing input area cannot overwrite a closer target");
        owner.Distance = .1f; controller.Refresh();
        Check(controller.Selected == owner && controller.Focused.Count == 2 && controller.ButtonCount == 1, "winning multi-cell object keeps its area markers as one logical target");
        controller.StandingArea = null; controller.StandingOwner = null; controller.Watering = null; controller.Candidates.Clear(); controller.Refresh();
        Check(controller.Selected == null && controller.ButtonCount == 0 && controller.Focused.Count == 0, "empty candidate set clears selection and button");

        var installation = new InstallationObject(); installation.RuntimeOccupiedCoordinates.Add(new(2, 0)); installation.RuntimeOccupiedCoordinates.Add(new(3, 0));
        float d1 = controller.Distance(installation, new Block { WorldPosition = new(200, 0, 0) }, default);
        float d2 = controller.Distance(installation, new Block { WorldPosition = new(-200, 0, 0) }, new(0, 100, 0));
        Check(Math.Abs(d1 - 2.25f) < .001f && d1 == d2, "installed footprint distance ignores fallback cell and vertical render extent");
        installation.RuntimeOccupiedCoordinates.Clear(); installation.transform.position = new(2, 0, 0);
        Check(Math.Abs(controller.Distance(installation, new Block { WorldPosition = new(100, 0, 0) }, default) - 2.25f) < .001f, "unplaced object uses its own stable transform and logical size");
        Console.WriteLine($"{passed} interaction focus checks passed.");
    }
}
