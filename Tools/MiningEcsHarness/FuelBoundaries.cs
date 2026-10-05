using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ItemDefinition
{
    public enum EnergyType { None, Burn, Electricity }
    public struct EnergyUseRequirement { public EnergyType energyType; public float useEnergyAmount; }
    public readonly List<EnergyUseRequirement> Requirements = new();
    public EnergyType energyType;
    public float energyAmount;
    public ArchetypeBoundary MapObjectArchetype = new();
    public int UseEnergyRequirementCount => Requirements.Count;
    public bool TryGetUseEnergyRequirement(int index, out EnergyUseRequirement requirement)
    { requirement = index >= 0 && index < Requirements.Count ? Requirements[index] : default; return index >= 0 && index < Requirements.Count; }
}
public sealed class ArchetypeBoundary { public readonly List<int> RenderParts = new() { 1 }; }
public partial class InputOutputModule
{
    public ItemDefinition BoundItemDefinition;
    public int ResolveItemId() => BoundItemDefinition?.id ?? -1;
    public T GetComponent<T>() where T : new() => new T();
}
public partial class MiningWorld
{
    private readonly Dictionary<Vector2Int, List<MiningMachineInstance>> observers = new();
    public void ObserveFuel(MiningMachineInstance miner) => Observe(miner.Placement.inputOutputState.inputEnergyCoordinates, miner);
}
public static partial class InputOutputModuleEnergyAreaController
{
    private static readonly Dictionary<Vector2Int, Dictionary<ItemDefinition.EnergyType, int>> registeredEnergyAreas = new();
    private static readonly HashSet<Vector2Int> placementBlockingAreas = new();
}
public partial class ProductionWorld
{
    public bool IsEnergyArea(Vector2Int coordinate, ItemDefinition.EnergyType type = ItemDefinition.EnergyType.None) => false;
    public bool AppendEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> result) => false;
}
public partial class TerrainGenerator { public bool IsFloorObjectCoordinateVirtualized(Vector2Int coordinate) => false; }
public partial class BoxObject { public int MinimumRetainedItemCount; }
public partial class Block
{
    public int Consumed;
    public Action Mutation;
    public int ConsumeInputAreaCenterObjectsAnimated(int item, int count, Vector3 target, float interval)
    {
        int taken = Math.Min(count, GetInputAreaCenterItemCount(item));
        Count -= taken; Consumed += taken; Mutation?.Invoke(); return taken;
    }
}
public partial class BlockStateStore
{
    public int GetSavedCenterExtractableItemCount(Vector2Int coordinate, int item) => Saved.GetValueOrDefault(coordinate)?.GetInputAreaCenterItemCount(item) ?? 0;
    public int GetSavedCenterItemCount(Vector2Int coordinate, int item) => GetSavedCenterExtractableItemCount(coordinate, item);
    public int RemoveSavedCenterItems(Vector2Int coordinate, int item, int count)
        => Saved.GetValueOrDefault(coordinate)?.ConsumeInputAreaCenterObjectsAnimated(item, count, default, 0) ?? 0;
}
