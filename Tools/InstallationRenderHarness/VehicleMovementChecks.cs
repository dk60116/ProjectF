using ProjectF.MapObjects;
using ProjectF.Rendering;
using UnityEngine;

static partial class Checks
{
    static void CheckVehicleMovement(InstallationBatchRenderer host, Action render, Material material)
    {
        var terrain = new TerrainGenerator(); TerrainGenerator.Active = terrain;
        foreach (Vehicle vehicle in new Vehicle[] { new Train(), new Handcart() })
        {
            vehicle.Add(new MeshRenderer { Materials = new[] { material }, Filter = new MeshFilter { sharedMesh = new Mesh() } });
            vehicle.ConfigurePlacementRuntime(new(0, 0), 0, new[] { new Vector2Int(0, 0) }, 73);
            terrain.SaveRuntimeInstallationState(vehicle);
            var handle = vehicle.RuntimeMapObjectHandle;
            var model = vehicle.Renderers[0];
            WorldVisualUpdateManager.Visible.Add(vehicle); render();
            Check(host.MatrixCount == 1 && model.forceRenderingOff, "stationary vehicle uses batch presentation");

            foreach (float x in new[] { 1f, 1.25f, 2f, 2.25f, 32f, -33f, -33.25f })
            {
                int saves = terrain.Saves;
                var previousCell = vehicle.RuntimeAnchorCoordinate;
                vehicle.Move(new(x, 0, 0)); render();
                Check(vehicle.RuntimeMapObjectHandle.IsValid && host.MatrixCount == 1 && model.forceRenderingOff,
                    "moving train/handcart remains rendered after crossing a cell or chunk boundary");
                Check(VirtualRenderBatchCollection.LastMatrix.m03 == x && terrain.LastPose.x == x,
                    "moving vehicle submits its current pose and updates the stored pose within a cell");
                Check(vehicle.RuntimePlacementSequence == 73 && vehicle.RuntimeMapObjectHandle == handle,
                    "movement preserves simulation order and entity identity");
                Check(terrain.Saves == saves,
                    "registered vehicle movement never falls back to full installation registration");
                if (previousCell != vehicle.RuntimeAnchorCoordinate)
                    Check(terrain.Blocks[previousCell].MapObject == null && terrain.Blocks[vehicle.RuntimeAnchorCoordinate].MapObject == vehicle,
                        "crossing a cell unbinds the old block and binds the new one");
            }
            WorldVisualUpdateManager.Visible.Clear(); render();
            Check(host.MatrixCount == 0, "offscreen moving vehicle submits no matrices");
            vehicle.Move(new(64, 0, 0)); WorldVisualUpdateManager.Visible.Add(vehicle); render();
            Check(host.MatrixCount == 1 && VirtualRenderBatchCollection.LastMatrix.m03 == 64,
                "returning to view after offscreen movement submits the new pose");
            for (int i = 0; i < 100; i++) { vehicle.Move(new(64.1f, 0, 0)); render(); }
            int steadySaves = terrain.Saves;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { vehicle.Move(new(64.1f, 0, 0)); render(); }
            Check(terrain.Saves == steadySaves && GC.GetAllocatedBytesForCurrentThread() == allocated,
                "warmed within-cell vehicle movement and rendering allocate nothing in the managed probe");
            InstallationBatchRenderer.Unregister(vehicle); WorldVisualUpdateManager.Visible.Clear();
            Check(!model.forceRenderingOff && host.RegisteredCount == 0, "vehicle release restores native draw after moving");
            foreach (var block in terrain.Blocks.Values) block.MapObject = null;
        }
        var firstMove = new Train(); firstMove.Move(new(3, 0, 0));
        Check(firstMove.RuntimeMapObjectHandle.IsValid && firstMove.RuntimeAnchorCoordinate == new Vector2Int(3, 0),
            "first move without placement registers a live train handle");
        InstallationBatchRenderer.Unregister(firstMove);
        int saved = terrain.Saves;
        var excluded = new Train { ExcludeFromTerrainPersistence = true };
        excluded.Move(new(4, 0, 0));
        Check(!excluded.RuntimeMapObjectHandle.IsValid && terrain.Saves == saved,
            "movement does not persist a train excluded from terrain storage");
        TerrainGenerator.Active = null;
        new Train().Move(new(7, 0, 0));
        Check(true, "coordinate refresh tolerates an unavailable terrain");
    }
}

public partial class InstallationObject
{
    private Vector2Int runtimeAnchorCoordinate;
    private int runtimeQuarterTurns;
    private long runtimePlacementSequence;
    private List<Vector2Int> runtimeOccupiedCoordinates;
    private static int activeInstanceVersion;
    public bool ExcludeFromTerrainPersistence;
    public Vector2Int RuntimeAnchorCoordinate => runtimeAnchorCoordinate;
    public int RuntimeQuarterTurns => runtimeQuarterTurns;
    public long RuntimePlacementSequence => runtimePlacementSequence;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => runtimeOccupiedCoordinates;
    public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
    { anchor = runtimeAnchorCoordinate; turns = runtimeQuarterTurns; return runtimeOccupiedCoordinates?.Count > 0; }
    private static void RegisterRuntimeCoordinateIndex(InstallationObject owner) { }
    private static void UnregisterRuntimeCoordinateIndex(InstallationObject owner) { }
    private static long ClaimPlacementSequence(long sequence) => sequence > 0 ? sequence : 1;
    private void RefreshInstalledDirectionFromCurrentTransform() { }
    private void OnPlacementRuntimeChanged() { }
}
public partial class Vehicle
{
    private readonly Vector2Int[] runtimeCoordinateBuffer = new Vector2Int[1];
    public virtual void Move(Vector3 position)
    {
        transform.localToWorldMatrix = Matrix4x4.TRS(position, Quaternion.identity, new(1, 1, 1));
        foreach (var renderer in Renderers) renderer.transform.localToWorldMatrix = transform.localToWorldMatrix;
    }
}
public partial class Train : Vehicle
{
    public override void Move(Vector3 position) { base.Move(position); RefreshRuntimeCoordinate(position); }
}
public partial class Handcart : Vehicle
{
    private TerrainGenerator ResolveTerrain() => TerrainGenerator.Active;
    private static int ResolveQuarterTurns(Quaternion rotation) => 0;
    public override void Move(Vector3 position) { base.Move(position); RefreshRuntimePlacement(position, Quaternion.identity); }
}
public sealed class Block
{
    public InstallationObject MapObject;
    public void SetMapObject(InstallationObject owner) => MapObject = owner;
}
public sealed class PoseStore
{
    public TerrainGenerator Terrain;
    public bool MoveLiveVehicle(Vehicle owner)
    {
        if (!Terrain.Handles.TryGetValue(owner, out var handle)) return false;
        owner.BindRuntimeMapObjectHandle(handle);
        Terrain.LastPose = new(owner.transform.localToWorldMatrix.m03, 0, 0);
        return true;
    }
    public bool UpdateLiveInstallationWorldPose(InstallationObject owner)
    { Terrain.LastPose = new(owner.transform.localToWorldMatrix.m03, 0, 0); return owner.RuntimeMapObjectHandle.IsValid; }
}
public partial class TerrainGenerator
{
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public readonly Dictionary<InstallationObject, MapObjectHandle> Handles = new();
    public readonly HashSet<InstallationObject> persistenceDirtyInstallations = new();
    private PoseStore resourceStateStore;
    public int Saves;
    public Vector3 LastPose;
    public static TerrainGenerator ResolveActive() => Active;
    private void EnsureResourceStateStore() { resourceStateStore ??= new PoseStore { Terrain = this }; }
    public bool TryGetLoadedBlock(Vector2Int cell, out Block block)
    {
        if (!Blocks.TryGetValue(cell, out block)) Blocks.Add(cell, block = new Block());
        return true;
    }
    public void SaveRuntimeInstallationState(InstallationObject owner)
    {
        if (owner.ExcludeFromTerrainPersistence) return;
        Saves++;
        owner.BindRuntimeMapObjectHandle(new MapObjectHandle(60, (int)owner.RuntimePlacementSequence, 1, owner.RuntimePlacementSequence));
        Handles[owner] = owner.RuntimeMapObjectHandle;
        LastPose = new(owner.transform.localToWorldMatrix.m03, 0, 0);
        TryGetLoadedBlock(owner.RuntimeAnchorCoordinate, out var block); block.SetMapObject(owner);
    }
}
