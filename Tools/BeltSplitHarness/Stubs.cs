namespace UnityEngine
{
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int zero => default;
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x+b.x,a.y+b.y);
    }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 forward => new(0,0,1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a, float f) => new(a.x*f,a.y*f,a.z*f);
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float distance) => b;
    }
}
public sealed class Spliterbelt { public Block Left, Right; }
public sealed class ConvayorBelt2F
{
    public sealed class Transform { public int rotation; }
    public Transform transform = new();
    public HashSet<UnityEngine.Vector2Int> Cells = new();
    public bool CoversCoordinate(UnityEngine.Vector2Int cell) => Cells.Contains(cell);
    public bool TryGetOutputDirection(int rotation, out UnityEngine.Vector2Int direction)
    { direction = new(0,1); return true; }
}
public sealed class TerrainGenerator
{
    public Dictionary<UnityEngine.Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(UnityEngine.Vector2Int cell, out Block block) => Blocks.TryGetValue(cell, out block);
}
public partial class Block
{
    private const int ConveyorSingleLineBackLaneIndex = 2, ConveyorSingleLineFrontLaneIndex = 0,
        ConveyorSideExitLaneIndex = 1, ConveyorStackLaneLimit = 4;
    public Block Next;
    public Spliterbelt Splitter;
    public ConvayorBelt2F Bridge, CenterBridge;
    public TerrainGenerator Terrain;
    public UnityEngine.Vector2Int coordinate;
    public UnityEngine.Vector2Int Coordinate => coordinate;
    public bool SideReceive, Corner, StorageAvailable;
    private bool TryGetNextConveyorBlock(out Block next) { next = Next; return next != null; }
    private bool TryGetRuntimeSplitter(out Spliterbelt splitter) { splitter = Splitter; return splitter != null; }
    private bool TryGetSplitterChannels(Spliterbelt splitter, out Block left, out Block right)
    { left = splitter.Left; right = splitter.Right; return left != null && right != null; }
    private bool TryGetBelt2FBridgeCenterBelt(out ConvayorBelt2F bridge) { bridge = CenterBridge; return bridge != null; }
    private bool TryGetConveyorItemBelt2F(int lane, out ConvayorBelt2F bridge)
    { bridge = CenterBridge != null ? (IsBelt2FBridgeLaneIndex(lane) ? CenterBridge : null) : Bridge; return bridge != null; }
    private bool TryResolveOwningTerrainGenerator(out TerrainGenerator terrain) { terrain = Terrain; return terrain != null; }
    private bool IsConveyorStackingEnabled() => true;
    private static bool IsActiveConveyorLaneIndex(int lane) => lane == 0 || lane == 2;
    private static bool IsBelt2FBridgeLaneIndex(int lane) => lane == 1 || lane == 3;
    private bool CanUseConveyorSideExitLane() => Bridge == null && CenterBridge == null && !Corner;
    private bool TryGetConveyorSideHandoffFlow(Block source, int lane, out UnityEngine.Vector2Int flow)
    { flow = default; return source != this && SideReceive && !Corner && Splitter == null; }
    private bool HasConveyorSideExitLane() => HasBeltSplitSideExitLane();
    private bool IsValidConveyorLaneIndex(int lane) => StorageAvailable && IsBeltSplitLane(lane);
    private bool IsCornerConveyor() => Corner;
    private bool TryGetConveyorLaneLayout(out int front, out int back) { front=0;back=2;return StorageAvailable; }
    private bool TryGetConveyorCornerLaneTransition(int lane, out int source, out int destination, out float progress)
    { source=2;destination=0;progress=0;return true; }
    private bool TryGetCornerConveyorHandoffWorldPosition(int source, int destination, out UnityEngine.Vector3 position)
    { position=GetConveyorLaneWorldPosition(destination);return true; }
    private bool TryGetConveyorCornerLaneCandidates(out int source, out int destination, out int innerSource, out int innerDestination)
    { source=2;destination=0;innerSource=innerDestination=-1;return Corner; }
    private bool TryGetPreferredCornerConveyorReceiveLaneIndex(UnityEngine.Vector3 position, out int lane)
    { lane=2;return IsValidConveyorLaneIndex(lane); }
    private UnityEngine.Vector3 GetConveyorLaneWorldPosition(int lane) => new(coordinate.x,
        CenterBridge != null && IsBelt2FBridgeLaneIndex(lane) ? 1 : 0, coordinate.y+(lane<2?0.25f:-0.25f));
    public bool ResolveSimulation(int lane) => TryResolveConveyorSuccessorUncached(lane, out _, out _, out _);
}
