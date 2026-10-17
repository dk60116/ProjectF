using System;
using System.IO;
using System.Collections.Generic;

namespace UnityEngine
{
    public readonly record struct Vector3(float x, float y, float z);
    public readonly record struct Vector2Int(int x, int y);
    public readonly record struct Quaternion(float x, float y, float z, float w)
    { public static Quaternion identity => new(0, 0, 0, 1); }
}
public static partial class SaveGameBinarySerializer
{
    public static byte[] Encode(PlayerSaveData player)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WritePlayer(writer, player);
        return stream.ToArray();
    }
    public static PlayerSaveData Decode(byte[] bytes, int version, out long consumed)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var result = ReadPlayer(reader, version);
        consumed = stream.Position;
        return result;
    }
}
static class SerializationChecks
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static int Main()
    {
        try { Run(); Console.WriteLine($"Equipment binary save checks passed: {checks}"); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    static void Run()
    {
        var data = new PlayerSaveData { hasPlayer = true, hasHealth = true, currentHealth = 75 };
        data.bagSlots.Add(new() { slotIndex = 0, itemId = 12, count = 7, capacity = 10 });
        data.handSlots.Add(new() { slotIndex = 0, itemId = 2, count = 1, capacity = 1 });
        data.craftingQueue.Add(new() { itemId = 5, outputCount = 3, planId = 7, isPlanFinal = true });
        data.equipmentSlots.Add(new() { slotIndex = 0, itemId = -1, count = 0, capacity = 1 });
        data.equipmentSlots.Add(new() { slotIndex = 1, itemId = 2, count = 1, capacity = 1, installationHealth = new() { 43 } });
        data.selectedEquipmentSlot = 1;
        data.ammunitionSlots.Add(new() { slotIndex = 0, itemId = 23, count = 77, capacity = 100 });
        var bytes = SaveGameBinarySerializer.Encode(data);
        var decoded = SaveGameBinarySerializer.Decode(bytes, 75, out long consumed);
        Check(consumed == bytes.Length, "Version 75 consumes exactly the complete player section");
        Check(decoded.selectedEquipmentSlot == 1 && decoded.ammunitionSlots[0].count == 77,
            "Binary save retains selected equipment and unused ammunition");
        Check(decoded.equipmentSlots.Count == 2 && decoded.equipmentSlots[0].itemId == -1, "Binary save preserves empty equipment indices");
        Check(decoded.equipmentSlots[1].itemId == 2 && decoded.equipmentSlots[1].count == 1, "Binary save preserves equipped identity and count");
        Check(decoded.equipmentSlots[1].installationHealth[0] == 43, "Binary save preserves individual equipment health");
        Check(decoded.bagSlots[0].count == 7 && decoded.handSlots[0].itemId == 2, "Ordinary bag and hand storage layout is unchanged");
        Check(decoded.craftingQueue[0].planId == 7 && decoded.currentHealth == 75, "Existing player fields remain aligned");
        data.equipmentSlots.Clear();
        data.ammunitionSlots.Clear();
        var emptyBytes = SaveGameBinarySerializer.Encode(data);
        var legacy74 = new byte[emptyBytes.Length - 2*sizeof(int)];
        Array.Copy(emptyBytes,legacy74,legacy74.Length);
        var older = SaveGameBinarySerializer.Decode(legacy74,74,out consumed);
        Check(consumed == legacy74.Length && older.selectedEquipmentSlot == -1 && older.ammunitionSlots.Count == 0,
            "Version 74 remains readable with no selected weapon or ammo");
        var legacyBytes = new byte[emptyBytes.Length - 3*sizeof(int)];
        Array.Copy(emptyBytes, legacyBytes, legacyBytes.Length);
        var legacy = SaveGameBinarySerializer.Decode(legacyBytes, 73, out consumed);
        Check(consumed == legacyBytes.Length && legacy.equipmentSlots.Count == 0, "Legacy version 73 does not read a nonexistent equipment list");
        Check(legacy.craftingQueue[0].planId == 7 && legacy.currentHealth == 75, "Legacy player fields still decode correctly");
        data.equipmentSlots = null;
        var nullBytes = SaveGameBinarySerializer.Encode(data);
        Check(SaveGameBinarySerializer.Decode(nullBytes, 75, out _).equipmentSlots.Count == 0, "Null equipment lists serialize as empty");
    }
}
