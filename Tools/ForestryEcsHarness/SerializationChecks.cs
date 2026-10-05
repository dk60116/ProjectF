using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using ProjectF.Simulation;

static class SerializationChecks
{
    static int checks;
    static readonly MethodInfo Write = typeof(SaveGameBinarySerializer).GetMethod("WriteInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly MethodInfo Read = typeof(SaveGameBinarySerializer).GetMethod("ReadInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static byte[] Encode(BlockStateStore.InstallationSaveState state)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        Write.Invoke(null, new object[] { writer, state }); return stream.ToArray();
    }
    static BlockStateStore.InstallationSaveState Decode(BinaryReader reader, int version)
        => (BlockStateStore.InstallationSaveState)Read.Invoke(null, new[] { reader, (object)version, Enum.ToObject(Read.GetParameters()[2].ParameterType, 0) });
    static byte[] Legacy70(BlockStateStore.InstallationSaveState state)
    {
        // v71 appends the logging process after the complete v70 installation record.
        // No mounted records here: each legacy record must be truncated independently.
        byte[] bytes = Encode(state);
        var process = state.loggingProcess;
        using var tail = new MemoryStream(); using var writer = new BinaryWriter(tail);
        writer.Write(process.Direction); writer.Write(process.HingeAngle); writer.Write(process.EmptyDirectionElapsed);
        writer.Write(process.ConsumedEnergyUnits); writer.Write(process.HasTarget);
        writer.Write(process.TargetCoordinate.X); writer.Write(process.TargetCoordinate.Y);
        writer.Write(process.TargetDefinitionKey ?? string.Empty);
        Array.Resize(ref bytes, bytes.Length - (int)tail.Length); return bytes;
    }
    static void Main()
    {
        Check(SaveGameData.CurrentVersion == 71, "logging save schema is version 71");
        var logging = new BlockStateStore.InstallationSaveState
        {
            itemId = 80, placementSequence = 123, anchorCoordinate = new(-3, 5),
            loggingTreeFilterInitialized = true, loggingMinimumGrowth = 2, loggingMaximumGrowth = 8,
            loggingProcess = new LoggingProcess
            {
                Direction = 3, HingeAngle = 173.5f, EmptyDirectionElapsed = .75f, ConsumedEnergyUnits = 123456789,
                HasTarget = true, TargetCoordinate = new(-4, 5), TargetDefinitionKey = "Resource_Pine tree"
            }
        };
        logging.loggingEnabledTreeDefinitionKeys.Add("Resource_Pine tree");
        var seed = new BlockStateStore.InstallationSaveState
        {
            itemId = 104, inputOutputState = new InputOutputModule.PersistentState
            {
                hasDeterministicUnits = true, seedPlanterPlantElapsedUnits = 5000, seedPlanterHasLoadedSeed = true,
                seedPlanterLoadedSeedItemId = 12, seedPlanterLoadedSeedInputCoordinate = new(7, 9),
                seedPlanterTransferRemainingUnits = 600
            }
        };
        logging.mountedInstallations.Add(new BlockStateStore.MountedInstallationSaveState { pointIndex = 1, installation = seed });
        using (var stream = new MemoryStream())
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            Write.Invoke(null, new object[] { writer, logging }); Write.Invoke(null, new object[] { writer, null });
            Write.Invoke(null, new object[] { writer, seed }); stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var restored = Decode(reader, 71);
            Check(restored.loggingProcess.Direction == 3 && restored.loggingProcess.HingeAngle == 173.5f
                && restored.loggingProcess.EmptyDirectionElapsed == .75f, "logger orientation and waiting phase round trip");
            Check(restored.loggingProcess.HasTarget && restored.loggingProcess.ConsumedEnergyUnits == 123456789
                && restored.loggingProcess.TargetCoordinate.Equals(new GridCell(-4, 5))
                && restored.loggingProcess.TargetDefinitionKey == "Resource_Pine tree", "logger work and target round trip");
            Check(restored.loggingMinimumGrowth == 2 && restored.loggingMaximumGrowth == 8
                && restored.loggingEnabledTreeDefinitionKeys.Count == 1, "logger filter round trip");
            Check(restored.mountedInstallations[0].installation.inputOutputState.seedPlanterHasLoadedSeed,
                "nested installation layout includes forestry process");
            Check(Decode(reader, 71) == null, "null installation layout stays aligned");
            var restoredSeed = Decode(reader, 71).inputOutputState;
            Check(restoredSeed.seedPlanterHasLoadedSeed && restoredSeed.seedPlanterLoadedSeedItemId == 12
                && restoredSeed.seedPlanterLoadedSeedInputCoordinate == new Vector2Int(7, 9)
                && restoredSeed.seedPlanterTransferRemainingUnits == 600 && restoredSeed.seedPlanterPlantElapsedUnits == 5000,
                "in-flight seed ownership and progress round trip");
            Check(stream.Position == stream.Length, "all current records consume exact payload");
        }
        logging.mountedInstallations.Clear();
        using (var stream = new MemoryStream())
        {
            stream.Write(Legacy70(logging)); stream.Write(Legacy70(seed)); stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var restored = Decode(reader, 70);
            Check(!restored.loggingProcess.HasTarget && restored.loggingProcess.ConsumedEnergyUnits == 0,
                "legacy logger has no invented unfinished work");
            Check(Decode(reader, 70).inputOutputState.seedPlanterHasLoadedSeed, "legacy planter ownership survives schema update");
            Check(stream.Position == stream.Length, "legacy records never consume v71 process fields");
        }
        var clone = logging.Clone(); clone.loggingProcess.ClearTarget(); clone.loggingEnabledTreeDefinitionKeys.Clear();
        Check(logging.loggingProcess.HasTarget && logging.loggingEnabledTreeDefinitionKeys.Count == 1,
            "edit/save snapshot clone has independent logging state");
        var truncated = Encode(logging); Array.Resize(ref truncated, truncated.Length - 1);
        bool rejected = false;
        try { using var stream = new MemoryStream(truncated); using var reader = new BinaryReader(stream); Decode(reader, 71); }
        catch (TargetInvocationException error) when (error.InnerException is EndOfStreamException) { rejected = true; }
        Check(rejected, "truncated forestry payload is rejected instead of silently resetting work");
        Console.WriteLine($"{checks} forestry binary serialization checks passed against the compiled production assembly. No editor launched.");
    }
}
