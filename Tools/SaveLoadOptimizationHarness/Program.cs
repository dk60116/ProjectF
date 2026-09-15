using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using ProjectF.Persistence;
using UnityEngine;

internal static class Program
{
    private static int checks;

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    private static void Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[]
            {
                Path.GetFullPath("FactorioProject/Library/ScriptAssemblies"),
                "C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine"
            })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        CheckScheduling();
        CheckLoadTimingLog();
        CheckSaveTimingLog();
        CheckDetachedBeltState();
        CheckChunkDependencies();
        CheckRuntimeOptimizationContracts();
        foreach (string path in args) CheckSaveFile(path);
        Console.WriteLine($"PASS {checks} save/load optimization checks");
    }

    private static void CheckScheduling()
    {
        double time = 0;
        var snapshot = new List<int>();
        bool disposed = false;
        IEnumerator Capture()
        {
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    snapshot.Add(i);
                    time += 0.125;
                    yield return null;
                }
            }
            finally { disposed = true; }
        }
        using (var scheduler = new SaveSnapshotScheduler(Capture(), 4, () => time))
        {
            Require(scheduler.RunSlice() && snapshot.Count == 32, "cheap checkpoints share one time budget");
            time += 100; // Simulate a slow render frame; it must not consume the next slice.
            while (scheduler.RunSlice()) time += 100;
            Require(snapshot.SequenceEqual(Enumerable.Range(0, 1000)), "sliced snapshot preserves all state and ordering");
            Require(scheduler.SliceCount == 32 && scheduler.CheckpointCount == 1000, "1000 checkpoints complete in 32 slices");
            Require(scheduler.ActiveMilliseconds == 125 && scheduler.MaxSliceMilliseconds == 4, "capture timing excludes inter-frame wait");
            Require(!scheduler.RunSlice(), "completed capture cannot run twice");
        }
        Require(disposed, "completed iterator is disposed");

        disposed = false;
        using (var scheduler = new SaveSnapshotScheduler(Capture(), 4, () => time)) scheduler.RunSlice();
        Require(disposed, "cancelled snapshot disposes suspended work");

        IEnumerator Failure()
        {
            try { yield return null; throw new InvalidOperationException("capture failure"); }
            finally { disposed = true; }
        }
        disposed = false;
        bool failed = false;
        using (var scheduler = new SaveSnapshotScheduler(Failure(), 4, () => time))
        {
            try { scheduler.RunSlice(); } catch (InvalidOperationException) { failed = true; }
        }
        Require(failed && disposed, "failed capture propagates and cleans up");

        IEnumerator Expensive() { time += 9; yield return null; snapshot.Add(-1); }
        using (var scheduler = new SaveSnapshotScheduler(Expensive(), 4, () => time))
        {
            Require(scheduler.RunSlice() && scheduler.MaxSliceMilliseconds == 9, "indivisible overrun yields immediately and is measured");
            Require(!scheduler.RunSlice() && snapshot[^1] == -1, "overrun resumes without losing tail work");
        }

        IEnumerator WrongWait() { yield return new object(); }
        failed = false;
        using (var scheduler = new SaveSnapshotScheduler(WrongWait(), 4, () => time))
        {
            try { scheduler.RunSlice(); } catch (InvalidOperationException) { failed = true; }
        }
        Require(failed, "Unity wait instructions are never swallowed as checkpoints");
        Console.WriteLine("PASS snapshot budget, ordering, cancellation, failure and overrun");
    }

    private static void CheckDetachedBeltState()
    {
        var original = new ConveyorItemLaneSaveState
        {
            itemId = 42, laneIndex = 2, hasMotion = true, progress = 0.625f,
            sourceLaneIndex = 0, destinationLaneIndex = 2,
            nativeBeltState = new ProjectF.Conveyors.BeltSavedLane
            {
                X = 17, Y = -4, Lane = 2, OriginX = 16, OriginY = -4, OriginLane = 2,
                CursorX = 18, CursorY = -4, CursorLane = 0
            }
        };
        var source = new List<ConveyorItemLaneSaveState> { original };
        var copy = (List<ConveyorItemLaneSaveState>)typeof(BlockStateStore)
            .GetMethod("CloneConveyorLaneStates", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { source, false });
        Require(copy.Count == 1 && copy[0].itemId == 42 && copy[0].laneIndex == 2, "store snapshot keeps occupied belt lane");
        Require(copy[0].progress == 0.625f && copy[0].nativeBeltState.CursorX == 18,
            "store snapshot retains in-flight movement and merge checkpoint");
        original.itemId = 99;
        original.nativeBeltState.CursorX = 999;
        source.Clear();
        Require(copy[0].itemId == 42 && copy[0].nativeBeltState.CursorX == 18,
            "resumed simulation cannot mutate detached belt or native checkpoint");
        Console.WriteLine("PASS detached belt item, movement and native checkpoint copy");
    }

    private static void CheckLoadTimingLog()
    {
        string resolvedWorkspaceLog = (string)typeof(SlotTimingLogSink)
            .GetMethod("FindWorkspaceLogDirectory", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Path.GetFullPath("FactorioProject/Assets") });
        Require(Path.GetFullPath(resolvedWorkspaceLog) == Path.GetFullPath("Tools/Log"),
            "editor data path resolves to the repository Tools/Log folder");

        string directory = Path.Combine(Path.GetTempPath(), "ProjectF-SlotLoadTiming-" + Guid.NewGuid().ToString("N"));
        long ticks = 100L * System.Diagnostics.Stopwatch.Frequency;
        SlotTimingLogSink.TimestampProvider = () => ticks;
        SlotTimingLogSink.LogDirectoryProvider = () => directory;
        SlotTimingLogSink.InfoSink = _ => { };
        SlotTimingLogSink.WarningSink = message => throw new Exception(message);

        Require(!SlotLoadTimingLog.HasActiveSession,
            "slot load timing starts without an active session");
        SlotLoadTimingLog.Begin(1, "runtime-scene-reload");
        Require(SlotLoadTimingLog.HasActiveSession,
            "slot load timing exposes its active instrumentation window");
        ticks += 2L * System.Diagnostics.Stopwatch.Frequency;
        SlotLoadTimingLog.MarkPayloadReady(1);
        SlotLoadTimingLog.RecordStageWork("records-apply", 125.5);
        SlotLoadTimingLog.RecordStageWork("chunks", 900.0);
        SlotLoadTimingLog.RecordStageWork("chunks", 1100.0);
        ticks += 3L * System.Diagnostics.Stopwatch.Frequency;
        SlotLoadTimingLog.Complete(1, "saved-world-ready");
        Require(!SlotLoadTimingLog.HasActiveSession,
            "slot load timing closes its instrumentation window");
        string path = Path.Combine(directory, "slot_02_load_times.log");
        string[] lines = File.ReadAllLines(path);
        Require(lines.Length == 2 && lines[0].StartsWith("completedUtc\tslot\tstatus"), "slot timing log writes one header");
        string[] fields = lines[1].Split('\t');
        Require(fields[1] == "2" && fields[2] == "Completed" && fields[3] == "runtime-scene-reload",
            "slot timing log identifies slot, status and source");
        Require(fields[4] == "5000.0" && fields[5] == "2000.0" && fields[6] == "3000.0",
            "slot timing splits total, read and scene/world time");
        Require(fields[7].Contains("stages=records-apply:125.5/125.5/1")
                && fields[7].Contains("chunks:2000.0/1100.0/2"),
            "slot load timing records stage active/max/step metrics");

        ticks += System.Diagnostics.Stopwatch.Frequency;
        SlotLoadTimingLog.Begin(1, "first");
        ticks += System.Diagnostics.Stopwatch.Frequency;
        SlotLoadTimingLog.Begin(2, "replacement");
        SlotLoadTimingLog.CancelActive("scene-load-replaced");
        Require(File.ReadAllLines(path).Count(line => line.Contains("\tCancelled\tfirst\t")) == 1,
            "a replaced load records cancellation in its own slot log");
        Require(File.ReadAllLines(Path.Combine(directory, "slot_03_load_times.log"))
                .Count(line => line.Contains("\tCancelled\treplacement\t")) == 1,
            "the active replacement can record external scene cancellation");
        Console.WriteLine("PASS per-slot total/read/scene-world timing logs and cancellation");
    }

    private static void CheckSaveTimingLog()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ProjectF-SlotSaveTiming-" + Guid.NewGuid().ToString("N"));
        long ticks = 200L * System.Diagnostics.Stopwatch.Frequency;
        SlotTimingLogSink.TimestampProvider = () => ticks;
        SlotTimingLogSink.LogDirectoryProvider = () => directory;
        SlotTimingLogSink.InfoSink = _ => { };
        SlotTimingLogSink.WarningSink = message => throw new Exception(message);

        SlotSaveTimingLog.Begin(0, "runtime-save");
        SlotSaveTimingLog.RecordStageWork("map-flush-live", 210.0);
        SlotSaveTimingLog.RecordStageWork("map-flush-live", 111.5);
        ticks += 4L * System.Diagnostics.Stopwatch.Frequency;
        SlotSaveTimingLog.MarkSnapshotComplete(0, 321.5, 5.2, 81, 932);
        ticks += 3L * System.Diagnostics.Stopwatch.Frequency / 4L;
        SlotSaveTimingLog.Complete(0);

        string path = Path.Combine(directory, "slot_01_save_times.log");
        string[] lines = File.ReadAllLines(path);
        Require(lines.Length == 2 && lines[0].Contains("snapshotActiveMs\tmaxSliceMs"),
            "slot save timing log writes its metric header once");
        string[] fields = lines[1].Split('\t');
        Require(fields[1] == "1" && fields[2] == "Completed" && fields[3] == "runtime-save",
            "slot save timing identifies slot, status and source");
        Require(fields[4] == "4750.0" && fields[5] == "4000.0" && fields[6] == "321.5"
                && fields[7] == "5.2" && fields[8] == "81" && fields[9] == "932" && fields[10] == "750.0",
            "slot save timing splits snapshot wall/active/frame metrics and file write time");
        Require(fields[11].Contains("stages=map-flush-live:321.5/210.0/2"),
            "slot save timing records stage active/max/step metrics");

        ticks += System.Diagnostics.Stopwatch.Frequency;
        SlotSaveTimingLog.Begin(0, "runtime-save");
        ticks += System.Diagnostics.Stopwatch.Frequency;
        SlotSaveTimingLog.Fail(0, "snapshot-failed: InvalidOperationException");
        Require(File.ReadAllLines(path).Count(line => line.Contains("\tFailed\truntime-save\t")) == 1,
            "failed saves append a diagnostic row to their slot log");
        Console.WriteLine("PASS per-slot save snapshot/write timing and failure logs");
    }

    private static void CheckChunkDependencies()
    {
        var map = new MapSaveData();
        var state = new BlockStateStore.InstallationSaveState
        {
            anchorCoordinate = new Vector2Int(-9, -1), itemId = 10,
            occupiedCoordinates = new List<Vector2Int> { new(80, 0) },
            inputOutputState = new InputOutputModule.PersistentState
            {
                inputItemAreas = new() { new(new Vector2Int(160, -8), 1) },
                outputCoordinates = new() { new(240, 0) },
                pipeInputCoordinates = new() { new(320, 0) },
                focusCoordinates = new() { new(400, 0) }
            }
        };
        map.installations.Add(new InstallationSaveEntry { state = state });
        map.plantedResources.Add(new PlantedResourceSaveEntry { coordinate = new(480, 0) });
        map.floorObjects.Add(new FloorObjectSaveEntry { coordinate = new(560, 0), itemIds = new() { 7 } });
        map.animals.Add(new AnimalSaveEntry { position = new Vector3(640, 0, 0), hasTarget = true, targetPosition = new Vector3(720, 0, 0) });
        var plan = new SavedWorldChunkPlan(8, new(-100, -100), new(100, 100));
        List<Vector2Int> result = plan.Build(map, new(0, 0), 1, _ => 10);
        var chunks = new HashSet<Vector2Int>(result);
        Require(chunks.Count == result.Count && result[0] == new Vector2Int(0, 0), "chunks are unique and near-player first");
        foreach (Vector2Int required in new[] { new Vector2Int(-2, -1), new(-3, -2), new(10, 0), new(20, -1),
                     new(30, 0), new(40, 0), new(50, 0), new(60, 0), new(70, 0), new(80, 0), new(90, 0) })
            Require(chunks.Contains(required), "missing factory/IO/crop/item/animal dependency " + required);
        Require(!chunks.Contains(new Vector2Int(95, 95)), "inactive explored terrain needs no initial view");
        Require(map.installations.Count == 1 && state.occupiedCoordinates.Count == 1, "planning does not modify save records");
        List<Vector2Int> bounded = new SavedWorldChunkPlan(8, new(-1, -1), new(1, 1)).Build(map, new(0, 0), 8);
        Require(bounded.Count == 9, "every dependency and view is clipped to map bounds");
        Require(plan.Build(null, new(0, 0), 0).Count == 1, "empty saves still create the player chunk and reset the plan");
        Console.WriteLine("PASS off-screen factory dependencies, negative coordinates and map boundaries");
    }

    private static void CheckRuntimeOptimizationContracts()
    {
        string utilityPole = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/UtilityPole.cs");
        string terrain = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Map/TerrainGenerator.cs");
        string tickWorld = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Simulation/Core/SimulationTickWorld.cs");
        string facilityFlow = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Simulation/Core/FacilityFlowBatch.cs");
        string steamGenerator = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Object/MapObj/InstallationObject/SteamGenerator.cs");
        string blockStateStore = File.ReadAllText(
            "FactorioProject/Assets/Scripts/Map/BlockStateStore.cs");

        Require(utilityPole.Contains("BeginTopologyRefreshBatch()")
                && utilityPole.Contains("EndTopologyRefreshBatch(bool rebuildDirtyTopology = true)")
                && terrain.Contains("UtilityPole.BeginTopologyRefreshBatch()")
                && terrain.Contains("UtilityPole.EndTopologyRefreshBatch(rebuildDirtyTopology)"),
            "world restoration must batch utility-pole topology refresh");
        Require(terrain.Contains("\"finalize-power-topology\""),
            "the deferred topology rebuild must remain visible in slot load timing");
        Require(utilityPole.Contains("BuildSpatialConnectionCandidates(false)")
                && utilityPole.Contains("BuildSpatialConnectionCandidates(true)")
                && utilityPole.Contains("GetConnectionSpatialCell")
                && !utilityPole.Contains("for (int j = i + 1"),
            "utility-pole candidates must use the spatial index instead of all pairs");
        Require(terrain.Contains("persistenceDirtyInstallationScratch.AddRange(persistenceDirtyInstallations)")
                && Regex.Matches(terrain,
                    @"InstallationObject\.CopyActiveInstances\(persistenceDirtyInstallationScratch\)").Count == 1,
            "repeated installation snapshots must consume only the dirty set");
        Require(tickWorld.Contains("IPersistenceDirtyTrackable")
                && terrain.Contains("MarkPersistenceStateDirty(InstallationObject installationObject)"),
            "simulation mutations must feed installation persistence dirty tracking");
        Require(!facilityFlow.Contains("SteamGenerationStartReserveSeconds")
                && facilityFlow.Contains("float required = Math.Max(FluidEpsilon, requested)"),
            "steam generation must require the current interval, not a start-only reserve");
        Require(!steamGenerator.Contains("hasSteamGenerationReserve")
                && steamGenerator.Contains("SetGenerationActive(generated)"),
            "generator power, status and visuals must share one committed state");
        Require(!blockStateStore.Contains("steamGenerator.TryGetAvailableElectricOutputRate"),
            "save capture must not persist a derived generator power state");
        Console.WriteLine("PASS deferred pole topology, spatial candidates and dirty-only installation capture");
    }

    private static void CheckSaveFile(string path)
    {
        SaveGameData data = SaveGameBinarySerializer.ReadFromFile(path);
        int chunkSize = 8;
        int half = Math.Max(16, data.terrain.mapSize) / 2;
        var min = new Vector2Int((int)Math.Floor(-half / 8d), (int)Math.Floor(-half / 8d));
        var max = new Vector2Int((int)Math.Floor((half - 1) / 8d), (int)Math.Floor((half - 1) / 8d));
        var center = new Vector2Int((int)Math.Floor(data.player.position.x / chunkSize), (int)Math.Floor(data.player.position.z / chunkSize));
        byte[] before = Serialize(data);
        var radiiByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string assetPath in Directory.EnumerateFiles("FactorioProject/Assets/Data/Items", "*.asset"))
        {
            string asset = File.ReadAllText(assetPath);
            Match name = Regex.Match(asset, @"(?m)^  itemName: (.+)$");
            Match radius = Regex.Match(asset, @"(?m)^  sprinklerRangeRadius: (\d+)");
            if (name.Success && radius.Success)
                radiiByName[name.Groups[1].Value.Trim().Trim('"')] = int.Parse(radius.Groups[1].Value);
        }
        // Runtime remaps saved IDs to current ItemDefinition assets before building the plan.
        var savedRadii = new Dictionary<int, int>();
        foreach (var item in data.itemCatalog)
            if (radiiByName.TryGetValue(item.itemName, out int radius)) savedRadii[item.itemId] = radius;
        var chunks = new HashSet<Vector2Int>(new SavedWorldChunkPlan(chunkSize, min, max).Build(
            data.map, center, 8, itemId => savedRadii.TryGetValue(itemId, out int radius) ? radius : 3));
        void RequireTile(Vector2Int tile)
            => Require(chunks.Contains(new Vector2Int((int)Math.Floor(tile.x / 8d), (int)Math.Floor(tile.y / 8d))), "saved activity is outside initial residency");
        foreach (InstallationSaveEntry entry in data.map.installations)
        {
            if (entry?.state == null) continue;
            RequireTile(entry.state.anchorCoordinate);
            foreach (Vector2Int tile in entry.state.occupiedCoordinates) RequireTile(tile);
        }
        Require(before.SequenceEqual(Serialize(data)), "residency planning preserves the complete serialized world, resources, fluids and explored history");
        Console.WriteLine($"PASS {Path.GetFileName(path)} explored={data.terrain.activeChunkCoordinates.Count} initial={chunks.Count} installations={data.map.installations.Count} resources={data.map.resources.Count}");
    }

    private static byte[] Serialize(SaveGameData data)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        typeof(SaveGameBinarySerializer).GetMethod("WriteSaveGameData", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { writer, data });
        return stream.ToArray();
    }
}
