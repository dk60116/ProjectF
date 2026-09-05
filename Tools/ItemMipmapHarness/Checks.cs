using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using ProjectF.EditorTools;

static class Checks
{
    static int count;
    static void Check(bool ok, string text) { count++; if (!ok) throw new Exception(text); }
    static Texture Texture(string path, bool mips = false, FilterMode filter = FilterMode.Bilinear)
    {
        var texture = new Texture { Path = path };
        AssetDatabase.Assets[path] = texture;
        AssetImporter.Importers[path] = new TextureImporter { mipmapEnabled = mips, filterMode = filter };
        return texture;
    }
    static TextureImporter Importer(Texture t) => (TextureImporter)AssetImporter.Importers[t.Path];
    static void Main()
    {
        Texture color = Texture("Assets/color.png");
        Texture flow = Texture("Assets/flow.png", true);
        Texture data = Texture("Assets/coordinate.png");
        Texture namedData = Texture("Assets/Belt_PathUV.png");
        Texture package = Texture("Packages/p.png");
        Texture unsupported = new Texture { Path = "Assets/runtime.renderTexture" };
        Texture failure = Texture("Assets/failure.png");
        Texture ready = Texture("Assets/ready.png", true, FilterMode.Trilinear);
        Texture spriteTexture = Texture("Assets/sprite.png");
        Texture unused = Texture("Assets/UI/Icon.png");
        Importer(failure).Fail = true;
        var portable = new Material { Path = "Assets/portable.mat", Textures = { ["_BaseMap"] = color } };
        var visible = new Material { Path = "Assets/model.mat", Textures = {
            ["_BaseMap"] = color, ["_FlowMap"] = flow, ["_Detail"] = data,
            ["_Other"] = namedData, ["_NormalMap"] = ready } };
        var dataMaterial = new Material { Path = "Assets/variant.mat", Textures = {
            ["_PathUvMap"] = data, ["_Package"] = package, ["_Runtime"] = unsupported, ["_Fault"] = failure } };
        var root = new GameObject { Path = "Assets/root.prefab", Renderers = new Renderer[] {
            new Renderer { sharedMaterials = new[] { visible, portable } } } };
        var variant = new GameObject { Path = "Assets/MapObject/variant.prefab", Renderers = new Renderer[] {
            new Renderer { sharedMaterials = new[] { dataMaterial } },
            new SpriteRenderer { sprite = new Sprite { texture = spriteTexture } } } };
        foreach (var asset in new ObjectAsset[] { root, variant, portable, visible, dataMaterial })
            AssetDatabase.Assets[asset.Path] = asset;
        AssetDatabase.Prefabs = new[] { variant.Path };
        AssetDatabase.Dependencies = new[] { root.Path, variant.Path, visible.Path, dataMaterial.Path, portable.Path, unused.Path };
        var definitions = new[] { new ItemDefinition { mapObject = new MapObject { gameObject = root, Path = root.Path }, portableMat = portable } };
        var result = ItemTextureMipmapUtility.ApplyAll(definitions);
        Check(result.Changed == 3 && result.Unchanged == 1 && result.Skipped == 4 && result.Failed == 1, "bulk result categories");
        Check(result.Textures == 9 && result.Materials == 3, "duplicate materials and textures counted once");
        Check(Importer(color).Imports == 1 && Importer(color).mipmapEnabled, "shared installed/portable texture imported once");
        Check(Importer(flow).filterMode == FilterMode.Trilinear, "color FlowMap remains eligible and filter-only change imports");
        Check(!Importer(data).mipmapEnabled && !Importer(namedData).mipmapEnabled, "data property and filename exclusion");
        Check(Importer(spriteTexture).Imports == 1 && GameObject.InactiveIncluded, "inactive child and sprite texture traversal");
        Check(!Importer(package).mipmapEnabled && !Importer(unused).mipmapEnabled, "package and unreferenced icon unchanged");
        Check(!Importer(failure).mipmapEnabled && Importer(failure).filterMode == FilterMode.Bilinear, "failed import restores settings for retry");
        Check(EditorUtility.Clears == 1, "progress UI always cleared");
        Importer(failure).Fail = false;
        result = ItemTextureMipmapUtility.ApplyAll(definitions);
        Check(result.Changed == 1 && result.Unchanged == 4 && result.Failed == 0, "repeat skips completed textures and retries failure");
        result = ItemTextureMipmapUtility.ApplyAll(definitions);
        Check(result.Changed == 0 && result.Unchanged == 5, "completed run is idempotent");
        EditorUtility.Cancel = true;
        result = ItemTextureMipmapUtility.ApplyAll(definitions);
        Check(result.Cancelled && result.Changed == 0 && EditorUtility.Clears == 4, "collection cancellation changes no importer and clears progress");
        EditorUtility.Cancel = false;
        EditorApplication.isPlayingOrWillChangePlaymode = true;
        bool rejected = false;
        try { ItemTextureMipmapUtility.ApplyAll(definitions); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && EditorUtility.Clears == 5, "play mode guarded and progress cleaned");
        Console.WriteLine($"PASS: {count} production SetMipmap collection/import checks. Managed editor doubles; no Unity launched or assets imported.");
    }
}
public class ItemDefinition { public MapObject mapObject; public Material portableMat; }
public class MapObject : ObjectAsset { public GameObject gameObject; }
namespace UnityEngine
{
    public class ObjectAsset { public string Path; }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public class Texture : ObjectAsset { }
    public class Material : ObjectAsset
    {
        public Dictionary<string,Texture> Textures = new();
        public string[] GetTexturePropertyNames() => Textures.Keys.ToArray();
        public Texture GetTexture(string name) => Textures[name];
    }
    public class Renderer { public Material[] sharedMaterials = Array.Empty<Material>(); }
    public class SpriteRenderer : Renderer { public Sprite sprite; }
    public class Sprite { public Texture texture; }
    public class GameObject : ObjectAsset
    {
        public static bool InactiveIncluded;
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        { InactiveIncluded |= includeInactive; return Renderers.OfType<T>().ToArray(); }
    }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a,b); }
}
namespace UnityEditor
{
    public static class EditorApplication { public static bool isPlayingOrWillChangePlaymode; }
    public static class EditorUtility
    {
        public static int Clears;
        public static bool Cancel;
        public static bool DisplayCancelableProgressBar(string title, string message, float progress) => Cancel;
        public static void ClearProgressBar() { Clears++; }
    }
    public class AssetImporter
    {
        public static Dictionary<string,AssetImporter> Importers = new();
        public static AssetImporter GetAtPath(string path) => Importers.TryGetValue(path, out var value) ? value : null;
    }
    public class TextureImporter : AssetImporter
    {
        public bool mipmapEnabled, Fail;
        public FilterMode filterMode;
        public int Imports;
        public void SaveAndReimport() { if (Fail) throw new Exception("Import failure"); Imports++; }
    }
    public static class AssetDatabase
    {
        public static Dictionary<string,ObjectAsset> Assets = new();
        public static string[] Prefabs, Dependencies;
        public static bool IsValidFolder(string path) => true;
        public static string[] FindAssets(string filter, string[] roots) => Prefabs;
        public static string GUIDToAssetPath(string guid) => guid;
        public static string GetAssetPath(ObjectAsset asset) => asset.Path;
        public static string[] GetDependencies(string[] paths, bool recursive) => Dependencies;
        public static T LoadAssetAtPath<T>(string path) where T : class => Assets.TryGetValue(path, out var asset) ? asset as T : null;
        public static bool WriteImportSettingsIfDirty(string path) => true;
    }
}

