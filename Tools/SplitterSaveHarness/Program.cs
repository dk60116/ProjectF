using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static void Main()
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
                splitterState = new Spliterbelt.PersistentState
                    { filterOutput = mode, nextInput = input, nextOutput = output, wheelRotationMask = wheels }
            };
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            write.Invoke(null, new object[] { writer, state });
            writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            var restored = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { reader, (object)52, current });
            if (restored.splitterState.filterOutput != mode || restored.splitterState.nextInput != input
                || restored.splitterState.nextOutput != output || restored.itemFilterMaskWords[0] != 1UL << 42
                || restored.splitterState.wheelRotationMask != wheels || stream.Position != stream.Length)
                throw new Exception("Splitter save round-trip mismatch");

            // Version 51 lacks the wheel field; version 50 lacks the entire splitter tail.
            byte[] bytes = stream.ToArray();
            using var previousStream = new MemoryStream(bytes, 0, bytes.Length - 4);
            using var previousReader = new BinaryReader(previousStream);
            var previous = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { previousReader, (object)51, current });
            if (previous.splitterState.wheelRotationMask != 0 || previous.splitterState.nextInput != input
                || previous.splitterState.nextOutput != output || previousStream.Position != previousStream.Length)
                throw new Exception("Version 51 splitter alignment changed");
            using var legacyStream = new MemoryStream(bytes, 0, bytes.Length - 17);
            using var legacyReader = new BinaryReader(legacyStream);
            var legacy = (BlockStateStore.InstallationSaveState)read.Invoke(null, new[] { legacyReader, (object)50, current });
            if (legacy.splitterState != null || legacy.itemFilterMaskWords[0] != 1UL << 42
                || legacyStream.Position != legacyStream.Length)
                throw new Exception("Legacy installation alignment changed");
            cases += 3;
        }
        Console.WriteLine($"PASS: {cases} production serializer installation round-trips, including versions 50/51 compatibility. No engine launched.");
    }
}
