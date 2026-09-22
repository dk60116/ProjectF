using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Run through Unity Pipeline run_script. Read-only; intentionally never updates
// cached SerializedObjects, imports assets, applies properties, or saves anything.
public static class CaptureLiveParentState
{
    public static string Main()
    {
        var output = new StringBuilder();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        output.AppendLine("UTC=" + DateTime.UtcNow.ToString("O") + " playing=" + EditorApplication.isPlaying);
        foreach (var window in Resources.FindObjectsOfTypeAll<ItemDataEditorWindow>())
        {
            output.AppendLine("WINDOW " + Describe(window));
            foreach (string name in new[] { "selectedItemId", "definitionsCacheVersion", "cachedParentInputOutputModuleItemOptionsVersion", "cachedSerializedDefinitionTarget", "cachedSerializedMapObjectTarget" })
            {
                var value = typeof(ItemDataEditorWindow).GetField(name, flags)?.GetValue(window);
                output.AppendLine(name + "=" + (value is UnityEngine.Object obj ? Describe(obj) : value?.ToString() ?? "null/absent"));
            }
            var cached = typeof(ItemDataEditorWindow).GetField("cachedSerializedMapObject", flags)?.GetValue(window) as SerializedObject;
            if (cached != null)
            {
                output.AppendLine("CACHED_SO target=" + Describe(cached.targetObject) + " modified=" + cached.hasModifiedProperties);
                DescribeParentProperty(output, cached);
                if (cached.targetObject is InputOutputModule module)
                    DescribeModule(output, "CACHED_TARGET", module);
            }
            var options = typeof(ItemDataEditorWindow).GetField("cachedParentInputOutputModuleItemOptions", flags)?.GetValue(window) as ItemDefinition[];
            var labels = typeof(ItemDataEditorWindow).GetField("cachedParentInputOutputModuleItemOptionContents", flags)?.GetValue(window) as GUIContent[];
            if (options != null)
                for (int i = 0; i < options.Length; i++)
                    output.AppendLine("OPTION " + i + " " + (labels != null && i < labels.Length ? labels[i]?.text : "") + " " + Describe(options[i]));
        }

        // Capture loaded objects before explicitly resolving any additional assets.
        foreach (var module in Resources.FindObjectsOfTypeAll<InputOutputModule>())
            if (module.name.IndexOf("Production machine", StringComparison.OrdinalIgnoreCase) >= 0)
                DescribeModule(output, "LOADED", module);

        foreach (string path in new[] {
            "Assets/Data/Items/Item_34_Production machine (Mk1).asset",
            "Assets/Data/Items/Item_35_Production machine (MK2).asset",
            "Assets/Data/Items/Item_108_Production machine (MK3).asset",
            "Assets/Data/Items/Item_110_Production machine (MK4).asset" })
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            output.AppendLine("DEFINITION " + Describe(definition));
            if (definition != null)
            {
                output.AppendLine("MAP " + Describe(definition.mapObject));
                if (definition.mapObject is InputOutputModule module)
                    DescribeModule(output, "ASSET", module);
            }
        }
        output.AppendLine("SELECTION " + Describe(Selection.activeObject));
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        output.AppendLine("PREFAB_STAGE " + (stage == null ? "none" : stage.assetPath));
        return output.ToString();
    }

    public static string AuditAll()
    {
        var output = new StringBuilder();
        var visited = new HashSet<InputOutputModule>();
        int mismatches = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Data/Items" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition == null || !(definition.mapObject is InputOutputModule module) || !visited.Add(module))
                continue;
            string path = AssetDatabase.GetAssetPath(module);
            // Only read source YAML. Never use it to overwrite the loaded reference.
            var match = Regex.Match(File.ReadAllText(path), @"(?m)^  parentInputOutputModuleItem: \{([^\r\n]*)\}");
            if (!match.Success)
            {
                output.AppendLine("UNVERIFIED (no local source field): " + path);
                continue;
            }
            var guidMatch = Regex.Match(match.Groups[1].Value, @"guid: ([a-fA-F0-9]{32})");
            string expected = guidMatch.Success ? guidMatch.Groups[1].Value : "";
            var parent = module.ParentInputOutputModuleItem;
            string actual = parent != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(parent)) : "";
            bool same = expected == actual;
            if (!same) mismatches++;
            output.AppendLine((same ? "PASS " : "FAIL ") + module.name + " source=" + expected + " loaded=" + actual + " dirty=" + EditorUtility.IsDirty(module));
        }
        output.AppendLine("Modules=" + visited.Count + " mismatches=" + mismatches);
        return output.ToString();
    }

    private static void DescribeModule(StringBuilder output, string label, InputOutputModule module)
    {
        output.AppendLine(label + " " + Describe(module) + " dirty=" + EditorUtility.IsDirty(module));
        output.AppendLine("PARENT " + Describe(module.ParentInputOutputModuleItem));
        using (var serialized = new SerializedObject(module))
            DescribeParentProperty(output, serialized);
    }

    private static void DescribeParentProperty(StringBuilder output, SerializedObject serialized)
    {
        var property = serialized.FindProperty("parentInputOutputModuleItem");
        output.AppendLine(property == null ? "PROPERTY absent" : "PROPERTY " + Describe(property.objectReferenceValue) + " entityId=" + property.objectReferenceEntityIdValue);
    }

    private static string Describe(UnityEngine.Object obj)
    {
        if (ReferenceEquals(obj, null)) return "managed-null";
        if (obj == null) return "unity-null type=" + obj.GetType().FullName + " entityId=" + obj.GetEntityId();
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId);
        return obj.name + " type=" + obj.GetType().FullName + " entityId=" + obj.GetEntityId() + " persistent=" + EditorUtility.IsPersistent(obj) + " path=" + AssetDatabase.GetAssetPath(obj) + " guid=" + guid + " localId=" + localId;
    }
}
