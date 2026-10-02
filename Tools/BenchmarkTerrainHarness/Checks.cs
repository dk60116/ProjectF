using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Execute the actual compiled terrain and save planner, without Unity native objects.
internal static class Checks
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Type terrainType, coordinateType;
    private static int passed;
    private static object Coordinate(int x, int y) => Activator.CreateInstance(coordinateType, x, y);
    private static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethods(Members).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
    private static object Field(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
    private static void Require(bool value, string message)
    { if (!value) throw new Exception(message); passed++; }
    private static void InitializeCollection(object target, string name)
        => Set(target, name, Activator.CreateInstance(target.GetType().GetField(name, Members).FieldType));

    public static void Main(string[] args)
    {
        string[] directories = File.ReadAllLines(args[1]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string directory in directories)
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        // Native profiler marker constructors cannot run outside Unity. Disable only
        // those initializers in a private in-memory copy; terrain method bodies stay intact.
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in directories) resolver.AddSearchDirectory(directory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        using var module = ModuleDefinition.ReadModule(Path.Combine(args[0], "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        var initializer = module.Types.Single(t => t.Name == "TerrainGenerator").Methods.Single(m => m.Name == ".cctor");
        foreach (var instruction in initializer.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Newobj || instruction.Operand is not MethodReference constructor
                || constructor.DeclaringType.FullName != "Unity.Profiling.ProfilerMarker") continue;
            if (instruction.Previous.OpCode != OpCodes.Ldstr || instruction.Next.OpCode != OpCodes.Stsfld)
                throw new Exception("Unexpected profiler initializer shape");
            instruction.OpCode = OpCodes.Pop; instruction.Operand = null;
            instruction.Next.OpCode = OpCodes.Nop; instruction.Next.Operand = null;
        }
        using var managedRuntime = new MemoryStream();
        module.Write(managedRuntime); managedRuntime.Position = 0;
        Assembly runtime = AssemblyLoadContext.Default.LoadFromStream(managedRuntime);
        terrainType = runtime.GetType("TerrainGenerator", true);
        coordinateType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector2Int", true);
        object terrain = RuntimeHelpers.GetUninitializedObject(terrainType);
        Set(terrain, "seed", 0); Set(terrain, "mapSize", 32);
        Set(terrain, "terrainSurfaceSubdivisions", 2);
        InitializeCollection(terrain, "profilingCloneTerrainSources");
        foreach (string name in new[] { "animalEligibleCoordinatesScratch", "animalHerdPlansScratch", "animalEligibleCoordinateLookup",
                     "animalChunkCoordinatesScratch", "animalUsedCoordinatesScratch" }) InitializeCollection(terrain, name);
        Set(terrain, "generateAnimals", true); Set(terrain, "animalDensity", 1f);
        Require((bool)terrainType.GetProperty("IsBenchmarkMap").GetValue(terrain), "seed zero selects benchmark policy");

        foreach (int distance in new[] { 0, 16, 256, 10000, 1000000, -16, -256, -10000, -1000000 })
        {
            object coordinate = Coordinate(distance, -distance);
            Require((bool)Invoke(terrain, "IsCoordinateWithinMapBounds", coordinate), "benchmark tile has no configured map limit");
            Require((bool)Invoke(terrain, "DoesChunkIntersectMapBounds", coordinate, 16), "benchmark chunks are accepted beyond finite map");
            Require(Invoke(terrain, "ClampChunkCoordinateToMapBounds", coordinate, 16).Equals(coordinate), "tracking does not stop at finite boundary");
            Require(Invoke(terrain, "GetTileBiome", coordinate).ToString() == "Dirt", "all benchmark tiles are dirt");
            Require(!(bool)Invoke(terrain, "IsWaterBiomeAt", coordinate), "public placement water query stays dry");
            Require(!(bool)Invoke(terrain, "IsRawWaterTileBiome", coordinate), "raw biome water query stays dry");
            Require((bool)Invoke(terrain, "IsBlockedForWater", coordinate), "legacy water generation is blocked");
            object[] resourceArgs = { coordinate, null };
            Require(!(bool)Invoke(terrain, "TryGetResourcePrefab", resourceArgs) && resourceArgs[1] == null, "no starter or ambient resources");
            Require((bool)Invoke(terrain, "BeginChunkAnimalSpawnWork", coordinate), "ambient animal generation ends immediately");

            object snapshot = Invoke(terrain, "CreateChunkSurfaceWorkerInput", coordinate, 16);
            Require(((Array)Field(snapshot, "biomeGrid")).Cast<object>().All(b => b.ToString() == "Dirt"), "surface worker receives dirt only across chunk seams");
            Require(((bool[])Field(snapshot, "oilGrid")).All(b => !b), "surface worker receives no oil terrain");
            Require((int)Field(snapshot, "mapMinX") < distance && (int)Field(snapshot, "mapMaxExclusiveX") > distance,
                "surface workers do not clip at configured map bounds");
        }

        object[] rangeArgs = { 16, null, null };
        Invoke(terrain, "GetMapChunkRange", rangeArgs);
        Type plannerType = runtime.GetType("ProjectF.Persistence.SavedWorldChunkPlan", true);
        object planner = Activator.CreateInstance(plannerType, 16, rangeArgs[1], rangeArgs[2]);
        foreach (int distance in new[] { 10000, -10000 })
        {
            object center = Coordinate(distance, -distance);
            var chunks = (IList)Invoke(planner, "Build", null, center, 2, null);
            Require(chunks.Count == 25 && chunks.Contains(center), "save restore planner retains distant benchmark player chunks");
        }
        object savedMap = Activator.CreateInstance(runtime.GetType("MapSaveData", true));
        object installation = Activator.CreateInstance(runtime.GetType("InstallationSaveEntry", true));
        object installationState = Activator.CreateInstance(runtime.GetType("BlockStateStore+InstallationSaveState", true));
        Set(installationState, "anchorCoordinate", Coordinate(160000, -160000));
        Set(installation, "state", installationState);
        ((IList)Field(savedMap, "installations")).Add(installation);
        var restoredChunks = (IList)Invoke(planner, "Build", savedMap, Coordinate(0, 0), 2, null);
        Require(restoredChunks.Contains(Coordinate(10000, -10000)), "saved factory outside finite map is retained by restore planner");

        object savedTerrain = Activator.CreateInstance(runtime.GetType("TerrainSaveData", true));
        Set(savedTerrain, "seed", 0); Set(savedTerrain, "mapSize", 32);
        Type serializer = runtime.GetType("SaveGameBinarySerializer", true);
        using var terrainBytes = new MemoryStream();
        using (var writer = new BinaryWriter(terrainBytes, System.Text.Encoding.UTF8, true))
            serializer.GetMethod("WriteTerrain", Members).Invoke(null, new[] { writer, savedTerrain });
        terrainBytes.Position = 0;
        using (var reader = new BinaryReader(terrainBytes, System.Text.Encoding.UTF8, true))
        {
            object loadedTerrain = serializer.GetMethod("ReadTerrain", Members).Invoke(null, new object[] { reader, 69 });
            Set(terrain, "seed", Field(loadedTerrain, "seed")); Set(terrain, "mapSize", Field(loadedTerrain, "mapSize"));
            Require((bool)terrainType.GetProperty("IsBenchmarkMap").GetValue(terrain)
                && (bool)Invoke(terrain, "IsCoordinateWithinMapBounds", Coordinate(1000000, 1000000)),
                "binary terrain save keeps seed-zero policy without new format fields");
        }
        foreach (int normalSeed in new[] { 1, -1, 12345 })
        {
            Set(terrain, "seed", normalSeed);
            Require(!(bool)terrainType.GetProperty("IsBenchmarkMap").GetValue(terrain), "other seeds use normal terrain policy");
            Require((bool)Invoke(terrain, "IsCoordinateWithinMapBounds", Coordinate(-16, 15)), "normal map includes its valid edge");
            Require(!(bool)Invoke(terrain, "IsCoordinateWithinMapBounds", Coordinate(16, 0)), "normal map keeps its finite limit");
            Require(!(bool)Invoke(terrain, "DoesChunkIntersectMapBounds", Coordinate(10000, 0), 16), "normal map rejects distant chunks");
            Require(Invoke(terrain, "ClampChunkCoordinateToMapBounds", Coordinate(10000, -10000), 16).Equals(Coordinate(0, -1)),
                "normal tracking remains clamped");
        }
        Console.WriteLine($"PASS {passed} benchmark terrain checks (actual runtime assembly; no game/UI launched)");
    }
}
