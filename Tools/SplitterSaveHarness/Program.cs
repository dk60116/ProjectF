using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private static void Main()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] {
                Path.GetDirectoryName(typeof(SaveGameBinarySerializer).Assembly.Location),
                Path.GetFullPath("FactorioProject/Library/ScriptAssemblies"),
                "C:/Program Files/Unity/Hub/Editor/6000.4.0f1/Editor/Data/Managed/UnityEngine" })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        RunChecks();
    }

    private static void RunChecks()
    {
        Type serializer = typeof(SaveGameBinarySerializer);
        MethodInfo write = serializer.GetMethod("WriteInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo read = serializer.GetMethod("ReadInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
        Type compatibilityType = read.GetParameters()[2].ParameterType;
        object current = Enum.ToObject(compatibilityType, 0);
        int cases = 0;
        for (int mode = 0; mode < 3; mode++)
        for (int input = 0; input < 2; input++)
        for (int output = 0; output < 2; output++)
        for (int wheels = 0; wheels < 4; wheels++)
        {
            var state = new BlockStateStore.InstallationSaveState
            {
                itemId = 46,
                itemName = "Spliter belt",
                itemFilterMaskInitialized = true,
                itemFilterMaskWords = new System.Collections.Generic.List<ulong> { 1UL << 42 },
                boxMinimumRetainedItemCount = wheels + 2,
                boxMaximumStoredItemCount = wheels + 5,
                loggingMinimumGrowth = wheels + 1,
                loggingMaximumGrowth = wheels + 4,
                splitterState = new Spliterbelt.PersistentState
                    { filterOutput = mode, nextInput = input, nextOutput = output, wheelRotationMask = wheels }
            };
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            write.Invoke(null, new object[] { writer, state });
            writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { reader, (object)56, current });
            if (restored.splitterState.filterOutput != mode || restored.splitterState.nextInput != input
                || restored.splitterState.nextOutput != output || restored.itemFilterMaskWords[0] != 1UL << 42
                || restored.splitterState.wheelRotationMask != wheels
                || restored.boxMinimumRetainedItemCount != wheels + 2
                || restored.boxMaximumStoredItemCount != wheels + 5
                || restored.loggingMinimumGrowth != wheels + 1 || restored.loggingMaximumGrowth != wheels + 4
                || stream.Position != stream.Length)
                throw new Exception("Splitter save round-trip mismatch");

            // Version 52 lacks the box reserve; version 51 also lacks the wheel field;
            // version 50 lacks the entire splitter tail.
            byte[] bytes = stream.ToArray();
            using var v55Stream = new MemoryStream(bytes, 0, bytes.Length - 8);
            using var v55Reader = new BinaryReader(v55Stream);
            var v55 = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { v55Reader, (object)55, current });
            if (v55.boxMinimumRetainedItemCount != wheels + 2
                || v55.boxMaximumStoredItemCount != BoxObject.DefaultMaximumStoredItemCount
                || v55.loggingMaximumGrowth != LoggingMachine.DefaultMaximumGrowth
                || v55Stream.Position != v55Stream.Length)
                throw new Exception("Version 55 must retain old minimums and default to unrestricted maximums");
            using var v52Stream = new MemoryStream(bytes, 0, bytes.Length - 12);
            using var v52Reader = new BinaryReader(v52Stream);
            var v52 = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { v52Reader, (object)52, current });
            if (v52.splitterState.wheelRotationMask != wheels
                || v52.boxMinimumRetainedItemCount != BoxObject.DefaultMinimumRetainedItemCount
                || v52Stream.Position != v52Stream.Length)
                throw new Exception("Version 52 installation alignment changed");
            using var previousStream = new MemoryStream(bytes, 0, bytes.Length - 16);
            using var previousReader = new BinaryReader(previousStream);
            var previous = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { previousReader, (object)51, current });
            if (previous.splitterState.wheelRotationMask != 0 || previous.splitterState.nextInput != input
                || previous.splitterState.nextOutput != output || previousStream.Position != previousStream.Length)
                throw new Exception("Version 51 splitter alignment changed");
            using var legacyStream = new MemoryStream(bytes, 0, bytes.Length - 29);
            using var legacyReader = new BinaryReader(legacyStream);
            var legacy = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { legacyReader, (object)50, current });
            if (legacy.splitterState != null || legacy.itemFilterMaskWords[0] != 1UL << 42
                || legacyStream.Position != legacyStream.Length)
                throw new Exception("Legacy installation alignment changed");
            cases += 5;
        }
        Console.WriteLine($"PASS: {cases} production serializer installation round-trips, including range bounds and versions 50/51/52/55 compatibility. No engine launched.");
    }
}
