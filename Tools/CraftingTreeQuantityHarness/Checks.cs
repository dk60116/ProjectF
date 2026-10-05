using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ProjectF.Crafting;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a,b);
        public static float Max(float a, float b) => Math.Max(a,b);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
    }
    public static class Debug
    {
        public static void LogWarning(string text) => Console.WriteLine(text);
        public static void LogError(string text) => throw new Exception(text);
    }
    public class Object { public static T FindAnyObjectByType<T>() where T : class => null; }
    public static class Application { public static string dataPath; }
    public sealed class TextAsset { public byte[] bytes; }
    public static class Resources
    {
        public static byte[] Data;
        public static T Load<T>(string name) where T : class => new TextAsset { bytes=Data } as T;
    }
    public static class GUI
    {
        public static void FocusControl(string name) { }
        public static void SetNextControlName(string name) { }
        public static string GetNameOfFocusedControl() => "";
    }
    public static class GUILayout
    {
        public static bool Button(string label, params object[] options) => false;
        public static object Width(float width) => null;
    }
}
namespace UnityEditor
{
    public static class EditorStyles { public static object miniButton; }
    public static class EditorGUILayout
    {
        public static int IntCalls, FloatCalls;
        public static float FloatField(float value, params object[] options) { FloatCalls++; return value; }
        public static int IntField(int value, params object[] options) { IntCalls++; return value; }
    }
    public static class AssetDatabase
    {
        public static string GetAssetPath(object definition) => "";
        public static string GUIDToAssetPath(string guid) => "";
    }
}
public sealed class ItemDefinition
{
    public int id;
    public string itemName;
    public string name => itemName;
    public bool fluid;
    public MapObject mapObject;
}
public sealed class MapObject
{
    public MapObject transform => this;
    public MapObject root => this;
    public object gameObject => this;
}
public sealed class ItemManager
{
    public struct ItemSet { public string name; public int id; }
    public IReadOnlyList<ItemDefinition> ItemDefinitions;
    public IReadOnlyList<ItemSet> ItemSets => null;
}
public sealed class GameManager
{
    public static GameManager Instance = new();
    public ItemManager ItemManger = new();
}
public static class ItemDefinitionLookup
{
    public static string GetPersistenceName(ItemDefinition item, IReadOnlyList<ItemDefinition> items) => item?.itemName ?? "";
    public static ItemDefinition ResolveByPersistenceName(IReadOnlyList<ItemDefinition> items, string name) => items.FirstOrDefault(x=>x.itemName==name);
}
public static class InputOutputModule
{
    public static bool IsFluidItemDefinition(ItemDefinition definition) => definition?.fluid==true;
    public readonly struct ItemIoEntry
    {
        public readonly ItemDefinition itemDefinition;
        public readonly float count;
        public ItemIoEntry(ItemDefinition definition, float count) { itemDefinition=definition; this.count=count; }
    }
}
public partial class EditorProbe
{
    private readonly Dictionary<int,float> outputCountByItemId = new();
    private readonly Dictionary<int,List<IngredientEntry>> recipes = new();
    private List<IngredientEntry> GetOrCreateRecipe(int id) => recipes[id];
    private List<MapObject> GetCraftingMapObjects(int id) => null;
    private static List<int> GetMapObjectRuntimeIds(List<MapObject> maps) => new();
    public void Save(string path, List<ItemDefinition> definitions, float output, float input)
    {
        outputCountByItemId[8]=output;
        recipes[8]=new() { new IngredientEntry(7,input) };
        WriteCraftingTree(path,new() {8},definitions);
    }
    public static float Field(float value, ItemDefinition definition) => DrawPositiveCountField("quantity",value,definition);
    public static (float output,float input) JsonRoundTrip(float output,float input)
    {
        var entry=new CraftingTreeJsonEntry { outputCount=output };
        entry.ingredients.Add(new CraftingIngredientJsonEntry { itemId=7,count=input });
        var options=new JsonSerializerOptions { IncludeFields=true };
        var restored=JsonSerializer.Deserialize<CraftingTreeJsonEntry>(JsonSerializer.Serialize(entry,options),options);
        return (restored.outputCount,restored.ingredients[0].count);
    }
}
public static partial class RemapProbe
{
    public static (float output,float input) Rewrite(string path,List<ItemDefinition> definitions)
    {
        var identities=definitions.ToDictionary(x=>x.id,x=>new DefinitionIdentity { persistenceName=x.itemName });
        if (!TryReadCurrentBinaryFile(path,identities,out var entries)) throw new Exception("Remapper read failed");
        WriteCurrentBinaryFile(path,entries,definitions);
        return (entries[0].outputCount,entries[0].ingredients[0].count);
    }
}
public static partial class AutoFillProbe
{
    public static (float output,float input,float prefabOutput) Load(List<ItemDefinition> definitions)
    {
        if (!TryLoadCraftingTreeBytes(definitions,out var entries)) throw new Exception("Auto fill read failed");
        var entry=entries[0];
        var definition=definitions.Find(x=>x.id==entry.itemId);
        var recipe=new RecipeEntry(new(),definition,entry.outputCount);
        return (entry.outputCount,entry.ingredients[0].count,recipe.outputs[0].count);
    }
}
public static class Checks
{
    private static int checks;
    private static void Require(bool condition,string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Equal(float actual,float expected,string message) => Require(Math.Abs(actual-expected)<.000001f,message);
    private static byte[] Legacy(int version)
    {
        using var stream=new MemoryStream();
        using var writer=new BinaryWriter(stream);
        writer.Write(version); writer.Write(1);
        if (version>=5) writer.Write("Lubricant"); else writer.Write(8);
        if (version>=3) writer.Write(0); else writer.Write("");
        if (version>=2) writer.Write(3);
        writer.Write(1);
        if (version>=5) writer.Write("Heavy Oil"); else writer.Write(7);
        writer.Write(2);
        return stream.ToArray();
    }
    private static void VerifyHandCraftingRecipes(List<ItemDefinition> definitions)
    {
        definitions.Add(new ItemDefinition { id=200, itemName="Station" });
        using var stream=new MemoryStream();
        using (var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
        {
            writer.Write(6); writer.Write(4);
            foreach (string name in new[] { "Plate", "Lubricant", "Station", "Heavy Oil" })
            {
                writer.Write(name);
                writer.Write(name=="Station"?1:0);
                if (name=="Station") writer.Write("Station");
                writer.Write(1f);
                writer.Write(name=="Heavy Oil"?0:1);
                if (name!="Heavy Oil") { writer.Write("Heavy Oil"); writer.Write(1f); }
            }
        }
        UnityEngine.Resources.Data=stream.ToArray(); CraftingTreeRuntime.ForceReload();
        var handRecipes=new List<int>(8) { -1 };
        Require(CraftingTreeRuntime.TryGetHandCraftableItemIds(handRecipes),"hand recipes exist");
        Require(handRecipes.SequenceEqual(new[] {28,100}),"hand recipes are sorted; station recipes and raw materials excluded");
        var stationRecipes=new List<int>();
        Require(CraftingTreeRuntime.TryGetCraftableItemIdsForMapObject(200,stationRecipes)
            && stationRecipes.SequenceEqual(new[] {200}),"station requirement reverse lookup stays exact");
        Require(!CraftingTreeRuntime.TryGetRequiredCraftingMapObjectIds(28,stationRecipes),"hand recipe does not acquire a station requirement");
        Require(CraftingTreeRuntime.TryGetRequiredCraftingMapObjectIds(200,stationRecipes)
            && stationRecipes.SequenceEqual(new[] {200}),"station recipe keeps its access requirement");
        Require(!CraftingTreeRuntime.TryGetHandCraftableItemIds(null),"null destination is rejected");
        for (int i=0;i<100;i++) CraftingTreeRuntime.TryGetHandCraftableItemIds(handRecipes);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for (int i=0;i<1000;i++) CraftingTreeRuntime.TryGetHandCraftableItemIds(handRecipes);
        Require(GC.GetAllocatedBytesForCurrentThread()==allocated,"cached hand recipe lookup allocates no memory after warmup");
        UnityEngine.Resources.Data=Array.Empty<byte>(); CraftingTreeRuntime.ForceReload();
        Require(!CraftingTreeRuntime.TryGetHandCraftableItemIds(handRecipes) && handRecipes.Count==0,"reload clears old hand recipes and destination");
    }

    public static void Main(string[] args)
    {
        var definitions=new List<ItemDefinition>
        {
            new() {id=8,itemName="Lubricant",fluid=true},
            new() {id=7,itemName="Heavy Oil",fluid=true},
            new() {id=100,itemName="Plate",fluid=false}
        };
        GameManager.Instance.ItemManger.ItemDefinitions=definitions;
        Equal(CraftingTreeQuantity.Normalize(.25f,definitions[0]),.25f,"fluid quantity preserves fraction");
        Equal(CraftingTreeQuantity.Normalize(.25f,definitions[2]),1f,"solid quantity stays whole");
        Equal(CraftingTreeQuantity.Normalize(2.25f,definitions[2]),2f,"solid quantity rounds only at item boundary");
        Equal(CraftingTreeQuantity.Normalize(float.NaN),1f,"invalid quantity is sanitized");
        Equal(CraftingTreeQuantity.Normalize(-1,definitions[0]),.0001f,"fluid quantity has a positive minimum");
        Require(default(CraftingTreeRuntime.IngredientEntry).count==0,"empty inventory ingredient remains empty");
        using (var legacyQuantity = new BinaryReader(new MemoryStream(new byte[4])))
            Equal(CraftingTreeQuantity.Read(legacyQuantity,5),1f,"legacy invalid count keeps its original minimum");
        Equal(EditorProbe.Field(.25f,definitions[0]),.25f,"fluid editor field preserves .25");
        Require(UnityEditor.EditorGUILayout.FloatCalls==1 && UnityEditor.EditorGUILayout.IntCalls==0,"fluid editor uses FloatField");
        Equal(EditorProbe.Field(2.25f,definitions[2]),2f,"solid editor uses integer quantity");
        Require(UnityEditor.EditorGUILayout.IntCalls==1,"solid editor uses IntField");
        var json=EditorProbe.JsonRoundTrip(.25f,.5f);
        Equal(json.output,.25f,"JSON output fraction"); Equal(json.input,.5f,"JSON input fraction");
        for (int version=1;version<=5;version++)
        {
            UnityEngine.Resources.Data=Legacy(version); CraftingTreeRuntime.ForceReload();
            Equal(CraftingTreeRuntime.GetOutputAmount(8),version>=2?3:1,"legacy output quantity v"+version);
            Require(CraftingTreeRuntime.TryGetIngredientsView(8,out var inputs),"legacy ingredients v"+version);
            Equal(inputs[0].amount,2f,"legacy input quantity v"+version);
        }
        var temp=Path.Combine(Path.GetTempPath(),"ProjectF-QuantityData-"+Guid.NewGuid().ToString("N"));
        var folder=Path.Combine(temp,"Data","CraftingTree"); Directory.CreateDirectory(folder);
        UnityEngine.Application.dataPath=temp;
        var path=Path.Combine(folder,"crafting_tree.bytes");
        new EditorProbe().Save(path,definitions,.25f,.5f);
        using (var reader=new BinaryReader(File.OpenRead(path))) Require(reader.ReadInt32()==6,"editor writes v6");
        UnityEngine.Resources.Data=File.ReadAllBytes(path); CraftingTreeRuntime.ForceReload();
        Equal(CraftingTreeRuntime.GetOutputAmount(8),.25f,"runtime fractional output");
        Require(CraftingTreeRuntime.TryGetIngredientsView(8,out var fractions),"runtime fractional input exists");
        Equal(fractions[0].amount,.5f,"runtime fractional input");
        Require(CraftingTreeRuntime.GetOutputCount(8)==1 && fractions[0].count==1,"inventory adapters keep integer quantities");
        var remapped=RemapProbe.Rewrite(path,definitions);
        Equal(remapped.output,.25f,"remapper preserves fractional output"); Equal(remapped.input,.5f,"remapper preserves fractional input");
        var auto=AutoFillProbe.Load(definitions);
        Equal(auto.output,.25f,"autofill fractional output"); Equal(auto.input,.5f,"autofill fractional input");
        Equal(auto.prefabOutput,.25f,"autofill generated pair keeps fraction below one");
        definitions[0].id=28; definitions[1].id=27;
        RemapProbe.Rewrite(path,definitions);
        UnityEngine.Resources.Data=File.ReadAllBytes(path); CraftingTreeRuntime.ForceReload();
        Equal(CraftingTreeRuntime.GetOutputAmount(28),.25f,"name-based reload after ID reorder");
        Require(CraftingTreeRuntime.TryGetIngredientsView(28,out var reordered) && reordered[0].itemId==27,"input name resolves changed ID");
        Equal(reordered[0].amount,.5f,"ID reorder keeps input fraction");
        VerifyHandCraftingRecipes(definitions);
        // Check the real source asset without rewriting existing recipe quantities.
        using var source=new BinaryReader(File.OpenRead(args[0]));
        int sourceVersion=source.ReadInt32(); int recipes=source.ReadInt32();
        Require(sourceVersion==5 || sourceVersion==6,"source uses supported name format");
        for (int i=0;i<recipes;i++)
        {
            source.ReadString(); int maps=source.ReadInt32();
            for (int j=0;j<maps;j++) source.ReadString();
            Require(CraftingTreeQuantity.Read(source,sourceVersion)>0,"source output quantity");
            int inputs=source.ReadInt32();
            for (int j=0;j<inputs;j++) {source.ReadString(); Require(CraftingTreeQuantity.Read(source,sourceVersion)>0,"source input quantity");}
        }
        Require(source.BaseStream.Position==source.BaseStream.Length,"real source binary consumed exactly");
        Console.WriteLine($"Crafting tree quantity checks passed: {checks}");
    }
}
