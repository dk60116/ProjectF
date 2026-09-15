using System;
using System.Collections.Generic;
using UnityEngine;

// Native scene access and recipe storage are boundaries; queries, registry maintenance
// and overlap rules are extracted from production. The collection pool is Unity's real pool.
public sealed class ModuleObject { public bool activeInHierarchy = true; }
public sealed class ItemDefinition
{
    public enum EnergyType { None, Chemical }
    public EnergyType energyType;
    public int energyAmount;
}
public sealed class BoxObject
{
    public int Allowed;
    public bool AcceptsItem(int itemId) => itemId == Allowed;
}
public sealed class Block { public object MapObject; }
public static class MapClimate { public static float CurrentTemperatureCelsius => 20f; }
public sealed class TerrainGenerator
{
    public static TerrainGenerator Active = new TerrainGenerator();
    public readonly Dictionary<Vector2Int, Block> Blocks = new();
    public bool TryGetLoadedBlock(Vector2Int coordinate, out Block block) => Blocks.TryGetValue(coordinate, out block);
}
public static class InputOutputModuleItemAreaController
{
    public static readonly HashSet<int> Accepted = new();
    public static bool TryGetAcceptedItemIds(Vector2Int coordinate, ISet<int> result)
    {
        foreach (int id in Accepted) result.Add(id);
        return Accepted.Count > 0;
    }
}
public static class InputOutputModuleEnergyAreaController
{
    public static bool TryGetAcceptedEnergyTypes(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> result) => false;
}
public partial class InputOutputModule
{
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeAreaCoordinates = new();
    private static readonly Dictionary<Vector2Int, HashSet<InputOutputModule>> registeredRuntimeFluidOutputCoordinates = new();
    public readonly ModuleObject gameObject = new();
    private readonly List<Vector2Int> runtimeInputEnergyCoordinates = new();
    private readonly List<Vector2Int> runtimeOutputCoordinates = new();
    private readonly List<Vector2Int> runtimePipeInputCoordinates = new();
    private readonly List<Area> runtimeInputItemAreas = new();
    private readonly HashSet<int> runtimeFluidOutputItemIdScratch = new();
    private sealed class Area { public Vector2Int coordinate; }
    public readonly List<int> Output = new();
    public readonly List<int> Accepted = new();
    public int OutputReads;
    public Action NestedQuery;
    private static readonly ItemDefinition fuel = new() { energyType = ItemDefinition.EnergyType.Chemical, energyAmount = 10 };
    private static readonly ItemDefinition solid = new();
    private static ItemDefinition ResolveItemDefinition(int id) => id == 3 ? fuel : solid;
    private static void InvalidateFluidTopologyCache() { }
    public void Place(Vector2Int coordinate, bool output = false, bool input = false, bool energy = false)
    {
        Remove();
        if (output) runtimeOutputCoordinates.Add(coordinate);
        if (input) runtimeInputItemAreas.Add(new Area { coordinate = coordinate });
        if (energy) runtimeInputEnergyCoordinates.Add(coordinate);
        RegisterRuntimeAreaCoordinates();
        RegisterRuntimeAreaCoordinates(); // Restore/OnEnable can register the same bindings.
        if (output)
        {
            if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(coordinate, out var modules))
                registeredRuntimeFluidOutputCoordinates.Add(coordinate, modules = new());
            modules.Add(this);
        }
    }
    public void Remove()
    {
        foreach (Vector2Int coordinate in runtimeOutputCoordinates)
        {
            if (!registeredRuntimeFluidOutputCoordinates.TryGetValue(coordinate, out var modules)) continue;
            modules.Remove(this);
            if (modules.Count == 0) registeredRuntimeFluidOutputCoordinates.Remove(coordinate);
        }
        UnregisterRuntimeAreaCoordinates();
        runtimeOutputCoordinates.Clear();
        runtimeInputItemAreas.Clear();
        runtimeInputEnergyCoordinates.Clear();
    }
    private bool ContainsRuntimeOutputCoordinate(Vector2Int coordinate) => runtimeOutputCoordinates.Contains(coordinate);
    private bool AppendOutputItemIds(ISet<int> result)
    {
        OutputReads++;
        NestedQuery?.Invoke();
        foreach (int id in Output) result.Add(id);
        return Output.Count > 0;
    }
    private static bool IsFluidItemId(int itemId) => itemId >= 0;
    public float GetStoredFluidTemperatureCelsius(int itemId) => 20f;
    private bool AppendRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> result) => AppendAcceptedRuntimeInputItemIdsAtCoordinate(coordinate, result);
    private bool AppendAcceptedRuntimeInputItemIdsAtCoordinate(Vector2Int coordinate, ISet<int> result)
    {
        foreach (Area area in runtimeInputItemAreas)
        {
            if (area.coordinate != coordinate) continue;
            foreach (int id in Accepted) result.Add(id);
            return true;
        }
        return false;
    }
    private bool AppendRuntimeInputEnergyTypesAtCoordinate(Vector2Int coordinate, ISet<ItemDefinition.EnergyType> result)
    {
        if (!runtimeInputEnergyCoordinates.Contains(coordinate)) return false;
        result.Add(ItemDefinition.EnergyType.Chemical);
        return true;
    }
}
public static class Checks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Main()
    {
        Vector2Int cell = new(100, 20), moved = new(101, 20);
        var producer = new InputOutputModule(); producer.Output.AddRange(new[] { 1, 2, 3 }); producer.Place(cell, output: true);
        var consumer = new InputOutputModule(); consumer.Accepted.Add(1); consumer.Place(cell, input: true);
        var irrelevant = new List<InputOutputModule>();
        for (int i = 0; i < 10000; i++)
        {
            var module = new InputOutputModule(); module.Output.Add(99); module.Place(new Vector2Int(i, -500), output: true); irrelevant.Add(module);
        }
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 1), "matching overlap rejected");
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2), "nonmatching overlap accepted");
        foreach (var module in irrelevant) Require(module.OutputReads == 0, "unrelated module scanned");
        producer.OutputReads = 0;
        Require(InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(
                cell,
                out int fluidItemId,
                out float fluidTemperature)
            && fluidItemId == 1
            && fluidTemperature == 20f,
            "indexed fluid output lookup failed");
        foreach (var module in irrelevant) Require(module.OutputReads == 0, "fluid lookup scanned unrelated module");
        consumer.Accepted.Clear(); consumer.Accepted.Add(2);
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 1), "stale recipe filter");
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2), "changed recipe rejected");
        consumer.Accepted.Clear();
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2), "empty accepted set must block overlap");
        consumer.Accepted.Add(2);
        InputOutputModuleItemAreaController.Accepted.Add(1);
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 1), "legacy area/controller union lost");
        InputOutputModuleItemAreaController.Accepted.Clear();
        var energy = new InputOutputModule(); energy.Place(cell, energy: true);
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 3), "fuel overlap rejected");
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 4), "non-output item accepted");
        TerrainGenerator.Active.Blocks[cell] = new Block { MapObject = new BoxObject { Allowed = 4 } };
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 4), "box filter must own storage");
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2), "box rejection ignored");
        TerrainGenerator.Active.Blocks.Clear();
        producer.gameObject.activeInHierarchy = false;
        var ids = new HashSet<int>();
        Require(!InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(cell, ids), "disabled producer still indexed as active");
        producer.gameObject.activeInHierarchy = true;
        producer.Place(moved, output: true);
        Require(!InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(cell, ids), "old coordinate remained after edit");
        Require(InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(moved, ids) && ids.SetEquals(new[] { 1, 2, 3 }), "restored/moved output missing");
        producer.Place(cell, output: true);
        bool inNested = false;
        producer.NestedQuery = () => {
            if (inNested) return;
            inNested = true;
            Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 3), "nested fuel query failed");
            inNested = false;
        };
        Require(InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2), "nested query corrupted outer set");
        Require(!InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 1), "nested query leaked allowed IDs");
        producer.NestedQuery = null;
        for (int i = 0; i < 1000; i++) InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) InputOutputModule.CanAddItemToRuntimeIoOverlapCoordinate(cell, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"steady queries allocated {allocated} bytes");
        for (int i = 0; i < 1000; i++)
            InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(cell, out _, out _);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            InputOutputModule.TryGetFluidOutputInfoAtRuntimeGridCoordinate(cell, out _, out _);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"steady fluid output queries allocated {allocated} bytes");
        producer.Remove(); ids.Clear();
        Require(!InputOutputModule.TryGetOutputItemIdsAtRuntimeGridCoordinate(cell, ids), "pooled producer still registered");
        Console.WriteLine("PASS: 10,000 unrelated modules untouched; indexed fluid output; overlap/filter/fuel/box/disable/edit/restore/remove/reentrant queries; general and fluid queries allocated 0 bytes.");
    }
}
