using System;
using System.Collections.Generic;
using ProjectF.Benchmark;
using UnityEngine;

public partial class TerrainGenerator
{
    internal sealed class BenchmarkInstallationTemplate
    {
        internal BlockStateStore.InstallationSaveState State;
        internal InstallationObject Prototype;
        internal ConveyorWorld.VisualPart[] VisualParts;
        internal Vector2Int[] FootprintOffsets, BlockingOffsets;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal int Turns;
        internal bool RequiresPlacementResolution;
        internal readonly List<Vector2Int> OccupiedScratch = new List<Vector2Int>();
    }

    internal BenchmarkInstallationTemplate CreateBenchmarkInstallationTemplate(
        InstallationPlacementController placement, InstallationObject source, int turns)
    {
        var template = new BenchmarkInstallationTemplate { Prototype = source, Turns = turns,
            Position = placement.GetInstalledObjectWorldPosition(Vector2Int.zero, source, turns),
            Rotation = placement.GetInstalledObjectRotation(source, turns), Scale = source.transform.localScale,
            RequiresPlacementResolution = source is Train || source is Railload };
        if (!(source is ConveyorBelt || source is Pipe || source is Building || source is RobotArm
            || source is MiningMachine miner && MiningWorld.Supports(miner)))
        {
            template.FootprintOffsets = placement.GetInstalledObjectFootprintCoordinates(Vector2Int.zero, source, turns).ToArray();
            template.BlockingOffsets = placement.GetInstalledObjectBlockingCoordinates(Vector2Int.zero, source, turns).ToArray();
            return template;
        }

        // Reuse normal configuration/capture once per type. Every installed entity
        // then goes directly into the existing authoritative data worlds.
        var proxy = CreateInstallationObject(source, transform);
        if (proxy == null) throw new InvalidOperationException("installation template could not be created");
        try
        {
            proxy.gameObject.SetActive(false);
            proxy.transform.SetPositionAndRotation(template.Position, template.Rotation);
            placement.ConfigureInstalledObjectRuntime(proxy, Vector2Int.zero, turns);
            EnsureResourceStateStore();
            if (!resourceStateStore.TryCaptureInstallationState(proxy, out var state) || state.occupiedCoordinates.Count == 0)
                throw new InvalidOperationException("installation template has no occupied cells");
            template.State = state;
            // Configuration may inherit footprint settings from a shared variant
            // family. Cache the configured proxy rather than its raw authoring data.
            template.FootprintOffsets = placement.GetInstalledObjectFootprintCoordinates(Vector2Int.zero, proxy, turns).ToArray();
            template.BlockingOffsets = state.occupiedCoordinates.ToArray();
            template.Scale = proxy.transform.localScale;
            if (source is ConveyorBelt belt)
                template.VisualParts = EnsureConveyorWorld().CaptureVisualParts(belt, template.Position, template.Rotation);
        }
        finally { ReleaseInstallationObject(proxy, source); }
        return template;
    }

    internal bool TryPlaceBenchmarkInstallation(InstallationPlacementController placement,
        BenchmarkInstallationTemplate template, Vector2Int coordinate)
    {
        for (int i = 0; i < template.FootprintOffsets.Length; i++)
        {
            if (!TryGetLoadedBlock(coordinate + template.FootprintOffsets[i], out var block) || block == null
                || block.MapObject != null || block.TryGetRuntimeConveyorRecord(out _)) return false;
        }
        if (template.State != null) return RegisterBenchmarkDataInstallation(template, coordinate);

        var installed = CreateInstallationObject(template.Prototype, transform);
        if (installed == null) throw new InvalidOperationException("installation could not be created");
        bool committed = false;
        try
        {
            var position = template.RequiresPlacementResolution
                ? placement.GetInstalledObjectWorldPosition(coordinate, template.Prototype, template.Turns)
                : template.Position + new Vector3(coordinate.x, 0f, coordinate.y);
            installed.transform.SetPositionAndRotation(position, template.Rotation);
            var occupied = template.OccupiedScratch; occupied.Clear();
            for (int i = 0; i < template.BlockingOffsets.Length; i++) occupied.Add(coordinate + template.BlockingOffsets[i]);
            if (template.RequiresPlacementResolution)
            {
                if (!placement.BindInstalledObjectToFootprintBlocks(installed, coordinate, template.Turns)) return false;
                placement.ConfigureInstalledObjectRuntime(installed, coordinate, template.Turns);
            }
            else
            {
                if (occupied.Count == 0) return false;
                for (int i = 0; i < occupied.Count; i++)
                    if (TryGetLoadedBlock(occupied[i], out var block)) block.SetMapObject(installed);
                placement.ConfigureBenchmarkInstalledObjectRuntime(installed, coordinate, template.Turns, occupied);
            }
            if (BenchmarkRuntime.ForceWorking) BenchmarkRuntime.Wake(installed);
            RegisterLiveInstallationObject(installed);
            committed = true;
            return true;
        }
        finally
        {
            if (!committed)
            {
                // An initialization failure must not leave an unregistered scene
                // object bound to the blocks already touched by this placement.
                for (int i = 0; i < template.FootprintOffsets.Length; i++)
                    if (TryGetLoadedBlock(coordinate + template.FootprintOffsets[i], out var block)
                        && block != null && ReferenceEquals(block.MapObject, installed)) block.SetMapObject(null);
                if (installed.TryGetPlacementRuntime(out _, out _)) resourceStateStore?.RemoveInstallation(installed);
                ReleaseInstallationObject(installed, template.Prototype);
            }
        }
    }

    private bool RegisterBenchmarkDataInstallation(BenchmarkInstallationTemplate template, Vector2Int coordinate)
    {
        var state = template.State.Clone();
        state.anchorCoordinate = coordinate;
        state.hasStorageKey = false;
        state.placementSequence = InstallationObject.ClaimNextPlacementSequence();
        OffsetBenchmarkCoordinates(state.occupiedCoordinates, coordinate);
        state.hasWorldPose = true;
        state.worldPosition = template.Position + new Vector3(coordinate.x, 0f, coordinate.y);
        state.worldRotation = template.Rotation;
        // Robot arm capture includes its authored IO areas. Keep those persisted
        // coordinates correct even though the arm world derives endpoints itself.
        var io = state.inputOutputState;
        if (io != null)
        {
            OffsetBenchmarkCoordinates(io.inputEnergyCoordinates, coordinate);
            OffsetBenchmarkCoordinates(io.outputCoordinates, coordinate);
            OffsetBenchmarkCoordinates(io.pipeInputCoordinates, coordinate);
            OffsetBenchmarkCoordinates(io.gridCoordinates, coordinate);
            OffsetBenchmarkCoordinates(io.focusCoordinates, coordinate);
            for (int i = 0; i < io.inputItemAreas.Count; i++)
            {
                var area = io.inputItemAreas[i]; area.coordinate += coordinate; io.inputItemAreas[i] = area;
            }
        }
        if (template.Prototype is Pipe pipe)
            return RegisterDataOnlyPipeState(state, pipe, state.worldPosition, template.Rotation, template.Scale);
        if (template.Prototype is Building building)
            return RegisterDataOnlyBuildingState(state, building, state.worldPosition, template.Rotation, template.Scale, out _);
        if (template.Prototype is RobotArm arm) return RegisterDataOnlyRobotArm(arm, state) != null;
        if (template.Prototype is MiningMachine miner) return RegisterDataOnlyMiningState(miner, state) != null;
        if (!(template.Prototype is ConveyorBelt belt)) return false;
        if (!resourceStateStore.RegisterDataOnlyInstallation(state, out var stored)) return false;
        // Integer tile translations preserve the modulo-one UV phase, allowing
        // immutable visual parts to be shared by every belt of this orientation.
        var record = EnsureConveyorWorld().Register(stored, belt, state.worldPosition, template.Rotation, template.Scale, template.VisualParts);
        if (record == null) { resourceStateStore.RemoveInstallation(BlockStateStore.GetInstallationStorageKey(stored)); return false; }
        BindLoadedBlocksToDataOnlyConveyor(record);
        return true;
    }

    private static void OffsetBenchmarkCoordinates(List<Vector2Int> coordinates, Vector2Int offset)
    {
        for (int i = 0; i < coordinates.Count; i++) coordinates[i] += offset;
    }
}
