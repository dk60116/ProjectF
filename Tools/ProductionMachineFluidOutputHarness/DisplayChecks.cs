using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white => new(1,1,1,1);
    }
    public readonly record struct Vector2(float x, float y)
    { public static Vector2 zero => new(0,0); public static Vector2 one => new(1,1); }
    public class Transform
    {
        public Transform parent;
        public GameObject gameObject;
        public readonly List<Transform> children = new();
        public int childCount => children.Count;
        public void SetParent(Transform value, bool worldPositionStays=false)
        { parent?.children.Remove(this); parent=value; value.children.Add(this); }
        public Transform Find(string name) => children.Find(t=>t.gameObject?.name==name);
        public T GetComponent<T>() => gameObject.GetComponent<T>();
        public int GetSiblingIndex() => parent.children.IndexOf(this);
        public void SetAsFirstSibling() => SetSiblingIndex(0);
        public void SetSiblingIndex(int index)
        { parent.children.Remove(this); parent.children.Insert(Math.Clamp(index,0,parent.children.Count),this); }
    }
    public sealed class RectTransform : Transform { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax; }
    public sealed class GameObject
    {
        public string name;
        public bool activeSelf;
        public readonly RectTransform transform;
        public readonly Image Fill;
        public readonly TextMeshProUGUI Text = new();
        public GameObject(string name, params Type[] components)
        { this.name=name; transform=new() { gameObject=this }; Fill=new() { gameObject=this }; }
        public GameObject(string name, Transform parent) : this(name) { transform.SetParent(parent); }
        public T GetComponent<T>() => typeof(T)==typeof(Image) ? (T)(object)Fill : (T)(object)transform;
    }
}
namespace UnityEngine.UI
{
    public sealed class Image
    {
        public enum Type { Simple, Filled }
        public enum FillMethod { Horizontal, Vertical }
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public float fillAmount;
        public Color color;
        public object sprite, material;
        public Type type;
        public FillMethod fillMethod;
        public int fillOrigin;
        public bool raycastTarget;
    }
}
namespace TMPro
{
    public sealed class TextMeshProUGUI
    { public string text; public Transform transform; public bool enableAutoSizing, enableWordWrapping; public float fontSizeMin, fontSizeMax; }
}
public static class MapClimate { public const float CurrentWaterTemperatureCelsius = 20; }
public sealed class ItemSlot
{
    public string Amount, Name;
    public void SetCustomDisplay(int id, object icon, string name, string amount) { Name=name; Amount=amount; }
}
public static class ItemManager { public readonly record struct ItemSet(string name, object icon); }
public partial class ItemInfoDescription
{
    private const string DefaultFluidItemName = "Water", ConvertedFluidFillName = "Production Converted Fluid";
    private static readonly Color FluidGaugeFillColor = new(0,0,1,1);
    private readonly List<ProductionFluidGauge> additionalProductionFluidGauges = new();
    private Image productionFluidConvertedFill;
    private readonly GameObject defaultGauge, inputItem, outputItem;
    private readonly Image defaultFill;
    private readonly TextMeshProUGUI defaultGaugeText;
    private readonly ItemSlot inputItemSlot = new(), outputItemSlot = new();
    private readonly List<GameObject> defaultItem = new();
    private readonly List<ItemSlot> defaultItemSlot = new();
    public ItemInfoDescription()
    {
        var parent = new Transform();
        inputItem=new("Input",parent); defaultGauge=new("Input gauge",parent); outputItem=new("Output",parent);
        defaultFill=defaultGauge.Fill; defaultGaugeText=defaultGauge.Text;
        defaultGaugeText.transform = new GameObject("Label", defaultGauge.transform).transform;
        for(int i=0;i<2;i++) { defaultItem.Add(new("Extra item",parent)); defaultItemSlot.Add(new()); }
    }
    public void Refresh(ProductionMachine machine) => TrySetProductionMachineItemSlots(machine,0);
    public string OutputRate => outputItemSlot.Amount;
    public string GaugeText => defaultGaugeText.text;
    public Image BaseFill => defaultFill;
    public Image ConvertedFill => productionFluidConvertedFill;
    public int ExtraGaugeCount => additionalProductionFluidGauges.Count;
    public bool ItemsHidden => !outputItem.activeSelf && !inputItem.activeSelf;
    public bool TextAboveConvertedFill => defaultGaugeText.transform.parent==productionFluidConvertedFill.transform.parent
        && defaultGaugeText.transform.GetSiblingIndex()>productionFluidConvertedFill.transform.GetSiblingIndex();
    public void DisturbColorOrder() => productionFluidConvertedFill.transform.SetSiblingIndex(1);
    public void ReuseColorLayer()
    { productionFluidConvertedFill=null; EnsureProductionConvertedFill(defaultFill,ref productionFluidConvertedFill); }
    private static GameObject Instantiate(GameObject template, Transform parent) => new("clone",parent);
    private static void ResolveGaugeReferences(GameObject root, ref Image fill, ref TextMeshProUGUI text)
    { fill=root.Fill; text=root.Text; }
    private static void SetGauge(GameObject root, Image fill, TextMeshProUGUI text, bool active, float ratio,
        Color color, float current, float max, bool oneDecimal=false, string textOverride=null)
    { if(root != null) root.activeSelf=active; if(fill != null) { fill.fillAmount=active?Math.Clamp(ratio,0,1):0; fill.color=color; }
      if(text != null) text.text=active?textOverride:string.Empty; }
    private static void SetActiveIfNeeded(GameObject root, bool active) { if(root != null) root.activeSelf=active; }
    private static T GetListItem<T>(List<T> list, int index) => list != null && index>=0 && index<list.Count ? list[index] : default;
    private static void ClearItemSlot(GameObject root, ItemSlot slot) { SetActiveIfNeeded(root,false); }
    private void MoveDefaultItemAfter(int index, GameObject preceding) => MoveItemAfter(GetListItem(defaultItem,index),preceding);
    private static float? ResolveModuleFluidTemperature(InputOutputModule module, int id) => 20f;
    private static string ResolveItemDisplayName(int id) => "Fluid"+id;
    private static ItemManager.ItemSet ResolveFluidItemSet(int id) => new(ResolveItemDisplayName(id),null);
    private static string ResolveFluidDisplayName(string name, float temperature) => name;
    private static void SetProductionIngredientItemSlot(GameObject root, ItemSlot slot, int id, int required,
        int stored, int capacity, float? temperature, float? storedLiters=null, float? requiredLiters=null) => SetActiveIfNeeded(root,true);
    private void SetProductionIngredientDefaultItemSlot(int index, int id, int required, int stored, int capacity,
        float? temperature, float? storedLiters=null, float? requiredLiters=null) => SetActiveIfNeeded(GetListItem(defaultItem,index),true);
    private static void SetItemSlot(GameObject root, ItemSlot slot, int id, int stored, int capacity,
        bool active, bool zero, float? temperature) { SetActiveIfNeeded(root,active); slot.Amount=$"{stored} / {capacity}"; }
}
public static class DisplayChecks
{
    private static int checks;
    private static void Require(bool condition, string scenario)
    { checks++; if(!condition) throw new Exception(scenario); }
    public static void Run()
    {
        CraftingTreeRuntime.Counts[8]=1;
        var maker=new ProductionMachine(2) { CraftSeconds=36 }; maker.SetIdle(); maker.SetInputLiters(18);
        var view=new ItemInfoDescription(); view.Refresh(maker);
        Require(view.OutputRate=="1.0L / s","fluid OutputItem retains the native per-second rate");
        Require(view.GaugeText=="Fluid9: 18.0 / 72.0 L" && view.BaseFill.fillAmount==.25f,
            "receiving shows actual input liters against the whole 72 L requirement");
        Require(view.BaseFill.color.r==1 && !view.ConvertedFill.gameObject.activeSelf,"receiving uses input color only");
        maker.SetInputLiters(0); maker.SetProcessing(0); view.Refresh(maker);
        Require(view.BaseFill.fillAmount==1 && view.ConvertedFill.fillAmount==0,"craft start retains the committed input in one full line");
        maker.SetProcessing(.25f); view.Refresh(maker);
        Require(view.BaseFill.color.r==1 && view.ConvertedFill.color.g==1 && view.ConvertedFill.fillAmount==.25f,
            "quarter progress has output color on the left and input color on the remaining area");
        Require(view.GaugeText=="Fluid9 → Fluid8: 9.0 / 36.0 L","conversion shows its generated-equivalent amount");
        Require(view.ConvertedFill.type==Image.Type.Filled && view.ConvertedFill.fillMethod==Image.FillMethod.Horizontal
                && view.ConvertedFill.fillOrigin==0 && !view.ConvertedFill.raycastTarget,
            "output layer fills from the left without intercepting UI input");
        Require(view.TextAboveConvertedFill,"color layer renders behind the label when text is a child of Fill");
        view.DisturbColorOrder(); view.Refresh(maker);
        Require(view.TextAboveConvertedFill,"refresh repairs a cached color layer placed above the label");
        view.DisturbColorOrder(); view.ReuseColorLayer();
        Require(view.TextAboveConvertedFill,"reusing an existing color layer restores text ordering");
        var overlay=view.ConvertedFill;
        maker.SetProcessing(.75f); view.Refresh(maker);
        Require(overlay.fillAmount==.75f && view.GaugeText.Contains("27.0 / 36.0"),"output color advances without allocating a new layer");
        maker.SetOutputting(); maker.Tick(); view.Refresh(maker);
        Require(view.BaseFill.color.g==1 && view.BaseFill.fillAmount==1 && !overlay.gameObject.activeSelf
                && view.GaugeText=="Fluid8: 36.0 / 36.0 L","completed output reuses the same full line in output color");
        var tank=new InstallationObject { Capacity=10 }; maker.Connect(tank);
        for(int i=0;i<110;i++) { MapObjectTickManager.CurrentSimulationTick+=6; maker.Tick(); }
        view.Refresh(maker);
        Require(view.GaugeText=="Fluid8: 26.0 / 36.0 L" && Math.Abs(view.BaseFill.fillAmount-26f/36)<.00001f,
            "output drains by shrinking the same line according to the actual remainder");
        Require(view.ExtraGaugeCount==0,"one-fluid recipe has no separate bottom gauge");
        for(int i=0;i<20;i++) view.Refresh(maker);
        Require(ReferenceEquals(overlay,view.ConvertedFill) && overlay.transform.parent.childCount==2,
            "live refresh reuses one child layer");
        maker.SetIdle(); view.Refresh(maker);
        Require(view.GaugeText=="Fluid9: 0.0 / 72.0 L" && view.BaseFill.fillAmount==0 && !overlay.gameObject.activeSelf,
            "after drain completion the same line returns to empty input reception");
        maker.HasRecipe=false; view.Refresh(maker);
        Require(view.ItemsHidden && view.GaugeText==string.Empty && !overlay.gameObject.activeSelf,
            "clearing the recipe hides its gauge and conversion layer");
        CraftingTreeRuntime.Counts.Clear();
        Console.WriteLine($"Production fluid display checks passed: {checks}");
    }
}
