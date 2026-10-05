using UnityEngine;
using System.Collections.Generic;
using ProjectF.Benchmark;

public partial class Block
{
    public Vector3 WorldPosition => default;
    public int VirtualConsumes;
    public bool PlayVirtualInputAreaConsumeAnimation(int item, Vector3 target, float delay = 0) { VirtualConsumes++; return true; }
    public bool TryAddDeferredOutput(int item, Vector3 start, float delay, bool center, out bool handled, ItemDefinition definition = null)
    {
        handled = true;
        if (!CanAddInputAreaCenterObjects(1, item)) return false;
        Item = item; Count++; return true;
    }
    public bool TryAddInputAreaCenterObjectAnimated(int item, Vector3 start, float delay, out object result)
    { result = null; return TryAddDeferredOutput(item, start, delay, true, out _); }
}

public partial class InputOutputModule
{
    public struct ItemIoEntry { public ItemDefinition itemDefinition; public int ResolvedItemCount; }
    public class InputOutputPair
    {
        public List<ItemIoEntry> inputs = new(), outputs = new();
    }
    public List<InputOutputPair> InputOutputPairs = new();
    public List<ItemIoEntry> InputList = new();
    public List<PersistentInputItemAreaState> runtimeInputItemAreas = new();
    public List<Vector2Int> runtimeInputEnergyCoordinates = new();
    public TerrainGenerator NativeTerrain = new();
    public BlockStateStore NativeStore = new();
    public ItemDefinition NativeDefinition = new();
    public int ActiveOutputItemId = 2;
    public bool hasActiveCraft = true;
    public bool IsBenchmarkWorking => BenchmarkRuntime.ForceWorking;
    private float InputConsumeMoveInterval => .1f;
    private TerrainGenerator ResolveTerrain() => NativeTerrain;
    private BlockStateStore ResolveBlockStateStore() => NativeStore;
    private Vector3 ResolveConsumeTargetWorldPosition() => default;
    private ItemDefinition ResolveInstalledDefinition() => NativeDefinition;
    public void FillNativeBenchmarkInputs() => RefreshBenchmarkInputs();
    public void ConsumeNativeBenchmarkInputs() => ConsumeBenchmarkInputs();
    public void ConsumeNativeBenchmarkEnergy() => SampleBenchmarkEnergy();
}
