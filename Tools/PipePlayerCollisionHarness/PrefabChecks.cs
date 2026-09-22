using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public static partial class Checks
{
    private static Dictionary<string, string> ReadObjects(string path) => Regex.Matches(File.ReadAllText(path),
        @"(?ms)^--- !u!\d+ &(\d+)\r?\n(.*?)(?=^---|\z)")
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    private static string Scalar(string source, string field) => Regex.Match(source,
        @"(?m)^  " + Regex.Escape(field) + @": ([^\r\n]+)").Groups[1].Value;
    private static string Reference(string source, string field) => Regex.Match(Scalar(source, field), @"fileID: (\d+)").Groups[1].Value;
    private static Vector3 Vector(string source, string field)
    {
        var values = Regex.Matches(Scalar(source, field), @"[xyz]: ([-+0-9.eE]+)");
        return new Vector3(float.Parse(values[0].Groups[1].Value, CultureInfo.InvariantCulture),
            float.Parse(values[1].Groups[1].Value, CultureInfo.InvariantCulture),
            float.Parse(values[2].Groups[1].Value, CultureInfo.InvariantCulture));
    }
    private static Vector3 Turn(Vector3 value, int turns)
    {
        for (int i = 0; i < turns; i++) value = new Vector3(value.z, value.y, -value.x);
        return value;
    }
    private static int ReadPlayerCollisionMask(string root)
    {
        var playerObjects = ReadObjects(Path.Combine(root, "FactorioProject/Assets/Prefab/Character/Player.prefab"));
        string body = playerObjects.Values.Single(value => value.StartsWith("Rigidbody:"));
        int layer = int.Parse(Scalar(playerObjects[Reference(body, "m_GameObject")], "m_Layer"));
        string physics = File.ReadAllText(Path.Combine(root, "FactorioProject/ProjectSettings/DynamicsManager.asset"));
        byte[] matrix = Convert.FromHexString(Scalar(physics, "m_LayerCollisionMatrix"));
        return BitConverter.ToInt32(matrix, layer * 4);
    }
    private static void CheckProductionMachineColliders(string root)
    {
        int mask = ReadPlayerCollisionMask(root);
        string basePath = Path.Combine(root, "FactorioProject/Assets/MapObject/InputOutputModule");
        string[] prefabs = Directory.GetFiles(basePath, "Production machine*.prefab", SearchOption.AllDirectories);
        Check(prefabs.Length == 4, "all ProductionMachine tiers must be checked");
        foreach (string prefab in prefabs)
        {
            var objects = ReadObjects(prefab);
            var boxes = objects.Values.Where(value => value.StartsWith("BoxCollider:")).ToArray();
            Check(boxes.Length > 0, "ProductionMachine needs an authored solid collider: " + prefab);
            foreach (string collider in boxes)
            {
                Check(Scalar(collider, "m_Enabled") == "1" && Scalar(collider, "m_IsTrigger") == "0",
                    "ProductionMachine collider must be solid and enabled: " + prefab);
                string owner = Reference(collider, "m_GameObject");
                int layer = int.Parse(Scalar(objects[owner], "m_Layer"));
                Check((mask & (1 << layer)) != 0,
                    "Player movement mask must include ProductionMachine collider layer: " + prefab);
            }
        }
    }

    private static void CheckAuthoredCorner(string root, PlayerController player)
    {
        player.Mask = ReadPlayerCollisionMask(root);
        var objects = ReadObjects(Path.Combine(root, "FactorioProject/Assets/MapObject/Fluid/Pipe/Pipe_Corner.prefab"));
        var transforms = objects.Values.Where(value => value.StartsWith("Transform:"))
            .ToDictionary(value => Reference(value, "m_GameObject"));
        var boxes = objects.Values.Where(value => value.StartsWith("BoxCollider:")).ToArray();
        Check(boxes.Length == 2, "authored corner retains both solid arms");
        for (int turns = 0; turns < 4; turns++)
        {
            var parts = new (Bounds bounds, int layer)[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                string collider = boxes[i];
                string owner = Reference(collider, "m_GameObject");
                Check(Scalar(collider, "m_Enabled") == "1" && Scalar(collider, "m_IsTrigger") == "0",
                    "authored corner collider must be solid and enabled");
                Vector3 center = Vector(collider, "m_Center");
                string transform = transforms[owner];
                // Current corner prefab has identity rotations/scales. Fail on edits
                // instead of silently testing bounds that disagree with the asset.
                while (Reference(transform, "m_Father") != "0")
                {
                    Check(Vector(transform, "m_LocalScale") == Vector3.one
                        && Scalar(transform, "m_LocalRotation") == "{x: 0, y: 0, z: 0, w: 1}",
                        "fixture requires authored identity child transforms");
                    center += Vector(transform, "m_LocalPosition");
                    transform = objects[Reference(transform, "m_Father")];
                }
                Vector3 size = Turn(Vector(collider, "m_Size"), turns);
                size = new Vector3(Math.Abs(size.x), Math.Abs(size.y), Math.Abs(size.z));
                parts[i] = (new Bounds(Turn(center, turns), size), int.Parse(Scalar(objects[owner], "m_Layer")));
            }
            PipeWorld.Current.Pipes.Clear();
            PipeWorld.Current.Pipes[Vector2Int.zero] = new PipeRuntimeRecord(Vector2Int.zero, parts);
            foreach (Vector2 direction in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
                Check(player.Sweep(-direction * 2, direction, 4, out _),
                    $"real corner prefab must block Player with real collision matrix: rotation={turns}, direction={direction}");
        }
        player.Mask = ~0;
        PipeWorld.Current.Pipes.Clear();
    }
}
