using System;
using System.IO;
using System.Reflection;

if (args.Length == 0)
{
    Console.WriteLine("Usage: RailSaveInspect <save-file> ...");
    Environment.ExitCode = 1;
    return;
}

// Reads existing saves with the compiled production serializer; never writes a slot.
foreach (string file in args)
{
    try
    {
        var save = SaveGameBinarySerializer.ReadFromFile(file);
        int rails = 0, emptyCoordinates = 0, missingPose = 0;
        Console.WriteLine($"{Path.GetFileName(file)} version={save.version} installations={save.map?.installations?.Count ?? 0}");
        if (save.map?.installations == null) continue;
        foreach (var entry in save.map.installations)
        {
            var state = entry?.state;
            if (state == null || (state.railVisualPathPoints?.Count ?? 0) < 2 && state.railRequiredItemCount <= 0) continue;
            rails++;
            if ((state.occupiedCoordinates?.Count ?? 0) == 0) emptyCoordinates++;
            if (!state.hasWorldPose) missingPose++;
            VerifyRoundTrip(state);
            if (rails <= 8)
                Console.WriteLine($"  rail item={state.itemId} name='{state.itemName}' sequence={state.placementSequence} points={state.railVisualPathPoints?.Count ?? 0} cells={state.occupiedCoordinates?.Count ?? 0} pose={state.hasWorldPose} anchor={state.anchorCoordinate} world={state.worldPosition}");
        }
        Console.WriteLine($"  rails={rails} emptyCoordinates={emptyCoordinates} missingPose={missingPose}");
        Console.WriteLine($"  PASS: {rails} rail records retained identity, geometry and pose through production binary round trips.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"{Path.GetFileName(file)}: {exception}");
        Environment.ExitCode = 1;
    }
}

static void VerifyRoundTrip(BlockStateStore.InstallationSaveState state)
{
    var serializer = typeof(SaveGameBinarySerializer);
    var write = serializer.GetMethod("WriteInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
    var read = serializer.GetMethod("ReadInstallationState", BindingFlags.Static | BindingFlags.NonPublic);
    using var bytes = new MemoryStream();
    using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        write.Invoke(null, new object[] { writer, state });
    bytes.Position = 0;
    using var reader = new BinaryReader(bytes);
    var restored = (BlockStateStore.InstallationSaveState)read.Invoke(null,
        new[] { reader, (object)SaveGameData.CurrentVersion, Enum.ToObject(read.GetParameters()[2].ParameterType, 0) });
    if (bytes.Position != bytes.Length || state.itemId != restored.itemId || state.itemName != restored.itemName
        || state.placementSequence != restored.placementSequence || state.anchorCoordinate != restored.anchorCoordinate
        || state.railRequiredItemCount != restored.railRequiredItemCount
        || state.railVisualPathExtendsStart != restored.railVisualPathExtendsStart
        || state.railVisualPathExtendsEnd != restored.railVisualPathExtendsEnd
        || state.hasWorldPose != restored.hasWorldPose
        || state.hasWorldPose && (!state.worldPosition.Equals(restored.worldPosition) || !state.worldRotation.Equals(restored.worldRotation))
        || !EqualValues(state.railVisualPathPoints, restored.railVisualPathPoints)
        || !EqualValues(state.occupiedCoordinates, restored.occupiedCoordinates))
        throw new InvalidDataException($"Rail {state.placementSequence} failed binary round trip.");
}

static bool EqualValues<T>(System.Collections.Generic.IReadOnlyList<T> a, System.Collections.Generic.IReadOnlyList<T> b)
{
    if (a == null || b == null) return a == null && b == null;
    if (a.Count != b.Count) return false;
    for (int i = 0; i < a.Count; i++)
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
    return true;
}
